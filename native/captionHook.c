// ============================================================
// PokerCaption native integration for Win32 messages and Cocos2d table scans.
//
// Esporta due hook:
//   hookProc       WH_CALLWNDPROC ? vede i messaggi *inviati* alle finestre
//                  del client (monitoraggio, notifiche al main)
//   hookGetMsgProc WH_GETMESSAGE  ? vede i messaggi *estratti dalla coda*;
//                  PostMessage(0x6742) = trigger del dispatcher comandi
//
// Memoria condivisa "Local\PokerCaption_SM" (creata dal main exe):
//   +0x74   HWND main PokerCaption
//   +0x78   flag busy (interlocked)
//   +0x7C   comando           (0x74 ping | 0x75/0x77/0x78/0x79 scan)
//   +0x88   indice tavolo     (0..41)
//   +0x8C   flag per-tavolo
//   +0x80578 + idx*0x2A00     slot tavolo (HWND al dword 0)
//   slot: +0x2668 posti | +0x19F8 count testi | +0x1A00 testi (18 x 160 B)
// ============================================================

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define MAP_NAME       L"Local\\PokerCaption_SM"
#define MAX_TABLES     42
#define OFF_MAINHWND   0x74
#define OFF_BUSY       0x78
#define OFF_CMD        0x7C
#define OFF_IDX        0x88
#define OFF_FLAG       0x8C
#define SLOT_BASE      0x80578u
#define SLOT_PITCH     0x2A00u

#include <tlhelp32.h>
#include <wchar.h>
#include <string.h>
#if defined(__i386__)
#include <MinHook.h>
#endif

#define MSG_CUSTOM_DISPATCH 0x6742     /* trigger dispatcher: PostMessage + WH_GETMESSAGE */
#define MSG_TABLE_SCAN      0x6743
#define MSG_NOTIFY          WM_APP     /* 0x401: notifica tavolo */

/* Risolve gli export Cocos2d e pubblica testi e coordinate dei nodi
   della scena in memoria condivisa. I puntatori ai metodi x86 usano __thiscall. */
typedef void *(__thiscall *cc_getRunningScene_t)(void *);
typedef void *(__thiscall *cc_getChildren_t)(void *);
typedef void *(__thiscall *cc_getName_t)(void *);
typedef void *(__thiscall *cc_getStringLabel_t)(void *);
typedef void *(__thiscall *cc_getStringText_t)(void *);
typedef void *(__thiscall *cc_getWorldPos_t)(void *, void *ret);
typedef void *(__thiscall *cc_getContentSize_t)(void *);
typedef unsigned char (__thiscall *cc_isVisible_t)(void *);
typedef void *(*cc_getInstance_t)(void);                     /* static: call normale */
typedef void *(__thiscall *cc_getGLView_t)(void *, unsigned idx);
typedef HWND (__thiscall *cc_getWin32Window_t)(void *);
typedef unsigned (__thiscall *cc_getNumGLViews_t)(void *);

static cc_getRunningScene_t p_getRunningScene;
static cc_getChildren_t     p_getChildren;
static cc_getName_t         p_getName;
static cc_getStringLabel_t  p_getStringLabel;
static cc_getStringLabel_t  p_getStringProxy;
static cc_getStringText_t   p_getStringText;
static cc_getWorldPos_t     p_getWorldPos;
static cc_getContentSize_t  p_getContentSize;
static cc_isVisible_t       p_isVisible;
static cc_getInstance_t     p_getInstance;
static cc_getGLView_t       p_getGLView;
static cc_getWin32Window_t  p_getWin32Window;
static cc_getNumGLViews_t   p_getNumGLViews;
static int g_cocos_bound = 0;
static int g_can_find_view = 0;
enum CocosScanStatus {
    SCAN_UNREPORTED, SCAN_MODULE_MISSING, SCAN_EXPORTS_MISSING,
    SCAN_DIRECTOR_MISSING, SCAN_SCENE_MISSING, SCAN_WALKED,
    SCAN_VIEW_MISSING, SCAN_SCENE_TYPE_MISMATCH, SCAN_VIEW_SCENE_MISSING
};
static enum CocosScanStatus g_bind_status = SCAN_UNREPORTED;

/* trova il modulo cocos2d del client: nome esatto, poi qualsiasi modulo
   con "cocos" nel nome (le skin possono rinominarlo) */
static HMODULE find_cocos_module(void)
{
    HMODULE h = GetModuleHandleW(L"libcocos2d.dll");
    if (h) return h;
    h = GetModuleHandleW(L"cocos2d.dll");
    if (h) return h;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, GetCurrentProcessId());
    if (snap == INVALID_HANDLE_VALUE) return NULL;
    MODULEENTRY32W me; me.dwSize = sizeof(me);
    HMODULE found = NULL;
    if (Module32FirstW(snap, &me)) {
        do {
            wchar_t low[MAX_MODULE_NAME32 + 1];
            int i = 0;
            for (; me.szModule[i] && i < MAX_MODULE_NAME32; i++)
                low[i] = (wchar_t)towlower(me.szModule[i]);
            low[i] = 0;
            if (wcsstr(low, L"cocos")) { found = me.hModule; break; }
        } while (Module32NextW(snap, &me));
    }
    CloseHandle(snap);
    return found;
}

static void cocos_bind(void)
{
    if (g_cocos_bound) return;
    HMODULE h = find_cocos_module();
    if (!h) { g_bind_status = SCAN_MODULE_MISSING; return; }
    /* Nomi degli export MSVC. */
    p_getRunningScene = (cc_getRunningScene_t)GetProcAddress(h, "?getRunningScene@Director@cocos2d@@QBEPAVScene@2@XZ");
    p_getInstance     = (void *)GetProcAddress(h, "?getInstance@Director@cocos2d@@SAPAV12@XZ");
    p_getGLView       = (cc_getGLView_t)GetProcAddress(h, "?getGLView@Director@cocos2d@@QBEPBUDirectorWindow@2@I@Z");
    p_getWin32Window  = (cc_getWin32Window_t)GetProcAddress(h, "?getWin32Window@GLViewImpl@cocos2d@@UAEPAUHWND__@@XZ");
    p_getNumGLViews   = (void *)GetProcAddress(h, "?getNumGLViews@Director@cocos2d@@QBEIXZ");
    p_getChildren     = (cc_getChildren_t)GetProcAddress(h, "?getChildren@Node@cocos2d@@UBEABV?$Vector@PAVNode@cocos2d@@@2@XZ");
    p_getName         = (cc_getName_t)GetProcAddress(h, "?getName@Node@cocos2d@@UBEABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@XZ");
    p_getStringLabel  = (cc_getStringLabel_t)GetProcAddress(h, "?getString@Label@cocos2d@@UBEABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@XZ");
    p_getStringProxy  = (cc_getStringLabel_t)GetProcAddress(h, "?getString@LabelProxy@cocos2d@@QBEABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@XZ");
    p_getStringText   = (cc_getStringText_t)GetProcAddress(h, "?getString@Text@ui@cocos2d@@QBEABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@XZ");
    p_getWorldPos     = (cc_getWorldPos_t)GetProcAddress(h, "?getWorldPosition@Widget@ui@cocos2d@@QBE?AVVec2@3@XZ");
    p_getContentSize  = (cc_getContentSize_t)GetProcAddress(h, "?getContentSize@Node@cocos2d@@UBEABVSize@2@XZ");
    p_isVisible       = (cc_isVisible_t)GetProcAddress(h, "?isVisible@Node@cocos2d@@UBE_NXZ");
    g_can_find_view = sizeof(void *) == 4 && p_getGLView && p_getNumGLViews && p_getWin32Window;
    g_cocos_bound = p_getInstance && (p_getRunningScene || g_can_find_view) &&
                    p_getChildren && p_getName && (p_getStringLabel || p_getStringProxy || p_getStringText);
    g_bind_status = g_cocos_bound ? SCAN_UNREPORTED : SCAN_EXPORTS_MISSING;
}

/* MSVC std::string (x86): [0..15] SSO buffer / ptr, [0x10] size, [0x14] capacity */
static int cc_str_read(const void *s, wchar_t *out, int max)
{
    if (!s || IsBadReadPtr(s, 0x18)) return 0;
    unsigned sz  = *(const unsigned *)((const char *)s + 0x10);
    unsigned cap = *(const unsigned *)((const char *)s + 0x14);
    if (sz == 0 || sz > 8192) return 0;
    const char *p = (cap < 16) ? (const char *)s : *(const char *const *)s;
    if (IsBadReadPtr(p, sz)) return 0;
    unsigned bytes = min(sz, (unsigned)(max - 1));
    while (bytes < sz && bytes > 0 && ((unsigned char)p[bytes] & 0xc0) == 0x80) bytes--;
    if (bytes == 0) return 0;
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, p, bytes, out, max - 1);
    if (n == 0) n = MultiByteToWideChar(CP_ACP, 0, p, bytes, out, max - 1);
    for (int i = 0; i < n; i++)
        if (out[i] < L' ') out[i] = L' ';
    out[n] = 0;
    return n;
}

/* area testi nello slot: count a +0x19F8, entry 160 B a +0x1A00 (18 × 160 = 0xB40)
   entry v2: hwnd,left,top,w,h,textLen,text[40],nameLen,name[24] — nome nodo cocos
   per il mapping semantico (es. bkg_totalpot → pot, Status → stack del posto) */
#define TEXT_MAX      18
#define TEXT_ENTRY    160
#define TEXT_BASE     0x1A00u
#define TEXT_COUNT    0x19F8u
#define TEXT_MAXCHARS 40
#define TEXT_NAMECHARS 24
#define BOARD_CARD_SLOTS 5
#define ACTION_SNAPSHOT_HEAD 0x27D4u
#define ACTION_HEAD          0x27D8u
#define ACTION_ACTIVE        0x27DCu
#define ACTION_RING          0x27E0u
#define ACTION_CAPACITY      64
/* Parallel array: old hosts can keep reading the original eight-byte entries. */
#define ACTION_TICKS         0x254Cu
#define ACTION_TICKS_VERSION 0x264Cu
#define ACTION_SNAPSHOT_TICK 0x2650u
#define ACTION_HOOK_MODE     0x2654u
#define ACTION_HOOKS_STATUS  0x2658u
#define BETSCRIPT_HERO_SEAT  0x265Cu
#define BETSCRIPT_AMOUNT     0x2660u
enum ActionHookMode {
    ACTION_HOOKS_DEFAULT, ACTION_SKIP_VISIBLE, ACTION_SKIP_ALL,
    ACTION_PROXY_ONLY = 4, ACTION_WIDGET_ONLY
};
_Static_assert(ACTION_TICKS + ACTION_CAPACITY * sizeof(DWORD) == ACTION_TICKS_VERSION,
               "action ticks must fit before the layout version");
_Static_assert(BETSCRIPT_AMOUNT + sizeof(DWORD) <= 0x2668u,
               "action timing must fit before the seat array");
#define OPTION_TRACE_ENABLE  0x19D4u
#define OPTION_TRACE_RESULT  0x19E0u
#define OPTION_TRACE_MAX_NODES 4096

enum OptionButtonTrace {
    OPTION_TRACE_WALKED = 1,
    OPTION_BACK_NODE = 2, OPTION_BACK_LABEL = 4,
    OPTION_WAITING_NODE = 8, OPTION_WAITING_LABEL = 16, OPTION_TRACE_LIMIT = 32
};

struct TextCtx {
    BYTE *slot;
    HWND table;
    int count, clientW, clientH, nodes, boardCards;
    void *board, *seats[11];
    unsigned optionTrace;
    const wchar_t *heroNick;
    int heroSeat, canSetAmount;
    float scaleX, scaleY;
};

static void texts_add(struct TextCtx *ctx, int x, int y, int w, int h, const wchar_t *txt, const wchar_t *nodeName, int seat)
{
    int len = 0;
    while (txt[len] && len < TEXT_MAXCHARS) len++;
    int nlen = 0;
    while (nodeName[nlen] && nlen < TEXT_NAMECHARS) nlen++;
    if (len == 0 && nlen == 0) return;
    int index = ctx->count;
    int replaceStatus = -1;
    for (int i = 0; seat && i < ctx->count; i++) {
        BYTE *prior = ctx->slot + TEXT_BASE + (SIZE_T)i * TEXT_ENTRY;
        if (*(DWORD *)(prior + 156) != (DWORD)seat) continue;
        int nameLength = *(int *)(prior + 104);
        const wchar_t *name = (const wchar_t *)(prior + 108);
        if (nameLength == nlen && wmemcmp(name, nodeName, nlen) == 0) { index = i; break; }
        if (nameLength == 6 && wmemcmp(name, L"Status", 6) == 0) replaceStatus = i;
    }
    if (index >= TEXT_MAX) {
        if (!ctx->heroSeat || seat == ctx->heroSeat) return;
        if (!seat) {
            for (int i = 0; i < ctx->count; i++) {
                BYTE *prior = ctx->slot + TEXT_BASE + (SIZE_T)i * TEXT_ENTRY;
                if (*(DWORD *)(prior + 156) == (DWORD)ctx->heroSeat || *(int *)(prior + 104) != 6) continue;
                if (wmemcmp((const wchar_t *)(prior + 108), L"Status", 6) == 0) { replaceStatus = i; break; }
            }
        }
        else if (wcscmp(nodeName, L"PlayerAction") != 0) return;
        if (replaceStatus < 0) return;
        index = replaceStatus;
    }
    BYTE *e = ctx->slot + TEXT_BASE + (SIZE_T)index * TEXT_ENTRY;
    *(DWORD *)(e + 0)  = 0;                       /* hwnd: non usato dal C# */
    *(DWORD *)(e + 4)  = (DWORD)x;
    *(DWORD *)(e + 8)  = (DWORD)y;
    *(DWORD *)(e + 12) = (DWORD)w;
    *(DWORD *)(e + 16) = (DWORD)h;
    *(DWORD *)(e + 20) = (DWORD)len;
    for (int i = 0; i < len; i++) *((wchar_t *)(e + 24) + i) = txt[i];
    *(DWORD *)(e + 24 + TEXT_MAXCHARS * 2) = (DWORD)nlen;
    for (int i = 0; i < nlen; i++) *((wchar_t *)(e + 28 + TEXT_MAXCHARS * 2) + i) = nodeName[i];
    *(DWORD *)(e + 156) = (DWORD)seat;
    if (index == ctx->count) ctx->count++;
}

struct Vec2T { float x, y; };

/* MSVC x86 RTTI: the complete object locator precedes the vtable. */
static const char *cocos_type_name(void *node)
{
    if (sizeof(void *) != 4 || IsBadReadPtr(node, sizeof(void *))) return NULL;
    const void *const *vtable = *(const void *const *const *)node;
    if (!vtable || IsBadReadPtr(vtable - 1, sizeof(void *))) return NULL;
    const BYTE *locator = (const BYTE *)vtable[-1];
    if (!locator || IsBadReadPtr(locator, 16) || *(const DWORD *)locator != 0) return NULL;
    const BYTE *descriptor = *(const BYTE *const *)(locator + 12);
    if (!descriptor || IsBadReadPtr(descriptor, 48)) return NULL;
    return (const char *)(descriptor + 8);
}

static int cocos_has_primary_base(void *node, const char *name)
{
    if (sizeof(void *) != 4 || !node || IsBadReadPtr(node, sizeof(void *))) return 0;
    const void *const *vtable = *(const void *const *const *)node;
    if (!vtable || IsBadReadPtr(vtable - 1, sizeof(void *))) return 0;
    const DWORD *locator = vtable[-1];
    if (!locator || IsBadReadPtr(locator, 20) || locator[0] != 0 || locator[1] != 0) return 0;
    const DWORD *hierarchy = (const DWORD *)(UINT_PTR)locator[4];
    if (!hierarchy || IsBadReadPtr(hierarchy, 16) || hierarchy[2] > 64) return 0;
    const DWORD *const *bases = (const DWORD *const *)(UINT_PTR)hierarchy[3];
    if (!bases || IsBadReadPtr(bases, hierarchy[2] * sizeof(*bases))) return 0;
    for (DWORD i = 0; i < hierarchy[2]; i++) {
        const DWORD *base = bases[i];
        if (!base || IsBadReadPtr(base, 16) || base[2] != 0 || base[3] != (DWORD)-1) continue;
        const char *descriptor = (const char *)(UINT_PTR)base[0];
        if (descriptor && !IsBadReadPtr(descriptor, 8 + strlen(name) + 1) &&
            strcmp(descriptor + 8, name) == 0) return 1;
    }
    return 0;
}

static void *table_scene(void *director, HWND table, enum CocosScanStatus *status)
{
    /* Aurora's x86 DirectorWindow owns a GLView at 0 and a Scene at 0x10. */
    unsigned count = p_getNumGLViews(director);
    *status = SCAN_VIEW_MISSING;
    for (unsigned i = 0; i < count; i++) {
        const BYTE *view_entry = p_getGLView(director, i);
        if (!view_entry || IsBadReadPtr(view_entry, 0x10 + sizeof(void *))) continue;
        void *view = *(void *const *)view_entry;
        const char *view_type = cocos_type_name(view);
        if (!view_type || strcmp(view_type, ".?AVGLViewImpl@cocos2d@@") != 0) continue;
        HWND window = p_getWin32Window(view);
        if (window != table && (!window || !IsChild(table, window))) continue;
        void *scene = *(void *const *)(view_entry + 0x10);
        *status = scene ? SCAN_SCENE_TYPE_MISMATCH : SCAN_VIEW_SCENE_MISSING;
        if (cocos_has_primary_base(scene, ".?AVScene@cocos2d@@"))
            return scene;
    }
    return NULL;
}

/* walker ricorsivo del scene graph: raccoglie le label (testo + world pos,
   convertita in top-left pixel della finestra) come entry testi nello slot */
enum TextRole { TABLE_OTHER, TABLE_POT, TABLE_STACK, TABLE_ACTION, TABLE_SEAT_ACTION };

static int read_board_cards(void *board)
{
    if (!board || IsBadReadPtr(board, 0x40) || !p_isVisible) return -1;
    void **children = p_getChildren(board);
    if (!children || IsBadReadPtr(children, 2 * sizeof(*children))) return -1;
    void **first = children[0], **last = children[1];
    if (!first || !last || last < first || last - first > 256) return -1;

    /* Placeholders can all be visible on the flop. Count the dealt cards in the first row. */
    int visible = 0, holders = 0;
    for (void **it = first; it < last && holders < BOARD_CARD_SLOTS; it++) {
        if (IsBadReadPtr(it, sizeof(*it))) break;
        void *holder = *it;
        const char *type = holder ? cocos_type_name(holder) : 0;
        if (!type || !strstr(type, "CocosCardHolder@@")) continue;
        holders++;
        if (!p_isVisible(holder)) continue;
        void **cards = p_getChildren(holder);
        if (!cards || IsBadReadPtr(cards, 2 * sizeof(*cards))) continue;
        void **card = cards[0], **end = cards[1];
        if (!card || !end || end <= card || IsBadReadPtr(card, sizeof(*card))) continue;
        const char *cardType = *card ? cocos_type_name(*card) : 0;
        if (cardType && strstr(cardType, "CocosStepSizeCard") && p_isVisible(*card)) visible++;
    }
    return holders == BOARD_CARD_SLOTS && (visible == 0 || visible >= 3) ? visible : -1;
}

static int read_label(void *node, wchar_t *text, int *isWidget)
{
    if (p_getStringLabel && cocos_has_primary_base(node, ".?AVLabel@cocos2d@@"))
        return cc_str_read(p_getStringLabel(node), text, TEXT_MAXCHARS);
    if (p_getStringProxy && cocos_has_primary_base(node, ".?AVLabelProxy@cocos2d@@"))
        return cc_str_read(p_getStringProxy(node), text, TEXT_MAXCHARS);
    if (p_getStringText && cocos_has_primary_base(node, ".?AVText@ui@cocos2d@@")) {
        *isWidget = 1;
        return cc_str_read(p_getStringText(node), text, TEXT_MAXCHARS);
    }
    return 0;
}

static void walk_node(void *node, int depth, struct TextCtx *ctx, int seat, enum TextRole role, unsigned optionNode)
{
    if (!node || depth > 24 || (!ctx->optionTrace && ctx->nodes >= 4096)) return;
    if (ctx->optionTrace && ctx->nodes >= OPTION_TRACE_MAX_NODES) {
        ctx->optionTrace |= OPTION_TRACE_LIMIT;
        return;
    }
    ctx->nodes++;
    if (IsBadReadPtr(node, 0x40)) return;
    if (p_isVisible && !p_isVisible(node)) return;

    /* figli: Vector<Node*> = 3 puntatori (begin/end/capac) */
    void *chRef = p_getChildren ? p_getChildren(node) : 0;
    void **begin = 0, **end = 0;
    int nchildren = -1;
    if (chRef && !IsBadReadPtr(chRef, 12)) {
        begin = *(void ***)chRef;
        end   = *((void ***)chRef + 1);
        if (begin == end)
            nchildren = 0;
        else if (begin && end && end > begin && (char *)end - (char *)begin <= 0x4000)
            nchildren = (int)(end - begin);
        else { begin = 0; nchildren = -1; }
    }

    /* nome nodo (virtuale Node: sicuro sui derivati) */
    wchar_t nameW[48]; nameW[0] = 0;
    if (p_getName) {
        void *nr = p_getName(node);
        if (!IsBadReadPtr(nr, 0x18)) cc_str_read(nr, nameW, 48);
    }
    if (ctx->optionTrace) {
        if (wcscmp(nameW, L"button_secondary_1") == 0) optionNode = OPTION_BACK_NODE;
        else if (wcscmp(nameW, L"button_secondary_2_2") == 0) optionNode = OPTION_WAITING_NODE;
        ctx->optionTrace |= optionNode;

        wchar_t text[TEXT_MAXCHARS + 1] = {0};
        int isWidget = 0;
        if (optionNode && read_label(node, text, &isWidget)) {
            if (optionNode == OPTION_BACK_NODE && _wcsicmp(text, L"I'm Back") == 0)
                ctx->optionTrace |= OPTION_BACK_LABEL;
            if (optionNode == OPTION_WAITING_NODE && _wcsicmp(text, L"Unjoin Waiting List") == 0)
                ctx->optionTrace |= OPTION_WAITING_LABEL;
        }
        if (begin && nchildren > 0)
            for (int i = 0; i < nchildren && i < 64; i++)
                if (!IsBadReadPtr(begin + i, 4))
                    walk_node(*(void **)(begin + i), depth + 1, ctx, 0, TABLE_OTHER, optionNode);
        return;
    }
    if (wcscmp(nameW, L"Board") == 0) {
        ctx->board = node;
        ctx->boardCards = read_board_cards(node);
    }

    if (wcsncmp(nameW, L"Seat ", 5) == 0) {
        int number = _wtoi(nameW + 5);
        if (number >= 1 && number <= 10) { seat = number; ctx->seats[number] = node; role = TABLE_OTHER; }
    }
    if (wcscmp(nameW, L"bkg_totalpot") == 0) role = TABLE_POT;
    else if (seat && wcscmp(nameW, L"Status") == 0) role = TABLE_STACK;
    else if (seat && wcscmp(nameW, L"Action") == 0) role = TABLE_SEAT_ACTION;
    else if (!seat && (wcsncmp(nameW, L"button_", 7) == 0 || wcscmp(nameW, L"Action") == 0))
        role = TABLE_ACTION;

    /* Call class-specific getters only for their matching concrete types. */
    wchar_t textW[TEXT_MAXCHARS + 1]; textW[0] = 0;
    int hasText = 0;
    int isWidget = 0;
    int findHero = seat && ctx->heroNick && !ctx->heroSeat;
    if (role != TABLE_OTHER || findHero) hasText = read_label(node, textW, &isWidget);
    if (hasText && findHero && _wcsicmp(textW, ctx->heroNick) == 0) ctx->heroSeat = seat;
    if (hasText && role == TABLE_ACTION &&
        (_wcsicmp(textW, L"Raise") == 0 || _wcsnicmp(textW, L"Raise To", 8) == 0 ||
         _wcsicmp(textW, L"Bet") == 0 || _wcsnicmp(textW, L"Bet ", 4) == 0))
        ctx->canSetAmount = 1;

    if (hasText && ((role == TABLE_POT && wcsncmp(textW, L"Pot:", 4) == 0) ||
        (role == TABLE_STACK && textW[0] >= L'0' && textW[0] <= L'9') ||
        (role == TABLE_ACTION && (wcscmp(textW, L"Check") == 0 || wcsncmp(textW, L"Call ", 5) == 0)) ||
        role == TABLE_SEAT_ACTION)) {
        const wchar_t *kind = role == TABLE_POT ? L"bkg_totalpot" : role == TABLE_STACK ? L"Status" :
                              role == TABLE_SEAT_ACTION ? L"PlayerAction" : L"Action";
        int x = 0, y = 0, w = 120, h = 20;
        if (isWidget && p_getWorldPos && !IsBadReadPtr(node, 0x100)) {
            struct Vec2T vp;
            p_getWorldPos(node, &vp);   /* sret: ret ptr sullo stack, this in ECX */
            if (vp.x > -20000 && vp.x < 20000 && vp.y > -20000 && vp.y < 20000) {
                x = (int)vp.x; y = (int)vp.y;
                void *csz = p_getContentSize ? p_getContentSize(node) : 0;
                if (csz && !IsBadReadPtr(csz, 8)) {
                    int cw = (int)*(float *)csz, chh = (int)*(float *)((char *)csz + 4);
                    if (cw > 0 && cw < 2000) w = cw;
                    if (chh > 0 && chh < 500) h = chh;
                }
            }
        }
        if (ctx->scaleX > 0 && ctx->scaleY > 0) {
            x = (int)(x * ctx->scaleX);
            y = (int)(y * ctx->scaleY);
            w = (int)(w * ctx->scaleX);
            h = (int)(h * ctx->scaleY);
        }
        if (ctx->clientH > 0 && y >= 0) y = ctx->clientH - y - h;   /* origine cocos in basso */
        /* Seat observations still count when their design coordinates are unavailable or outside the viewport. */
        if (seat || ctx->clientW <= 0 || (x >= -50 && x < ctx->clientW && y >= -20 && y < ctx->clientH))
            texts_add(ctx, x, y, w > 0 ? w : 120, h > 0 ? h : 20, textW, kind, seat);
        role = TABLE_OTHER;
    }

    /* ricorsione sui figli */
    if (begin && nchildren > 0) {
        int n = nchildren > 64 ? 64 : nchildren;
        for (int i = 0; i < n; i++)
            if (!IsBadReadPtr(begin + i, 4))
                walk_node(*(void **)(begin + i), depth + 1, ctx, seat, role, optionNode);
    }
}

static HANDLE  g_map     = NULL;
static BYTE   *g_shared  = NULL;
static HINSTANCE g_self  = NULL;
static volatile LONG g_busy = 0;
/* contatori diagnostici */
static volatile LONG g_calls = 0;      /* chiamate a hookProc (nCode==HC_ACTION) */
static volatile LONG g_dispatch = 0;   /* dispatch eseguiti */
static volatile LONG g_shared_ok = 0;  /* shared memory agganciata */
static volatile LONG g_last_msg = 0;   /* ultimo messaggio visto (CALLWNDPROC) */
static volatile LONG g_msg_calls = 0;  /* chiamate a hookGetMsgProc */
static volatile LONG g_replies = 0;    /* risposte ping 0x402 inviate */

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = inst;
        DisableThreadLibraryCalls(inst);
    } else if (reason == DLL_PROCESS_DETACH && !reserved) {
        if (g_shared) UnmapViewOfFile(g_shared);
        if (g_map) CloseHandle(g_map);
    }
    return TRUE;
}

static BYTE *shared_attach(void)
{
    if (g_shared) return g_shared;
    if (!g_map) g_map = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, MAP_NAME);
    if (!g_map) return NULL;
    g_shared = (BYTE *)MapViewOfFile(g_map, FILE_MAP_ALL_ACCESS, 0, 0, 0);
    if (g_shared) g_shared_ok = 1;
    return g_shared;
}

static BYTE *slot_ptr(BYTE *sh, int idx)
{
    if (idx < 0 || idx >= MAX_TABLES) return NULL;
    return sh + SLOT_BASE + (SIZE_T)idx * SLOT_PITCH;
}

#if defined(__i386__)
struct ActionTable {
    HWND hwnd;
    void *scene, *board, *seats[11];
    int cards;
    int last_seat, last_code, last_cards;
};

typedef void *(__thiscall *cc_node_getter)(void *);
typedef void (__thiscall *cc_set_text)(void *, const void *);
typedef void (__thiscall *cc_set_visible)(void *, unsigned char);
static cc_node_getter action_parent;
static cc_set_text original_proxy_text, original_widget_text;
static cc_set_visible original_visible;
static struct ActionTable action_tables[MAX_TABLES];
static SRWLOCK action_lock = SRWLOCK_INIT;
static int action_capture;
static unsigned action_hooks_status;

static volatile LONG action_text_seen, action_root_seen, action_seat_seen;
static int action_code(const wchar_t *text)
{
    static const wchar_t *const labels[] = {
        L"Fold", L"Check", L"Call", L"Bet", L"Raise", L"Limp", L"All-in", L"Post"
    };
    for (int i = 0; i < sizeof(labels) / sizeof(*labels); i++) {
        size_t size = wcslen(labels[i]);
        if (_wcsnicmp(text, labels[i], size) == 0 && (!text[size] || iswspace(text[size]))) return i + 1;
    }
    return _wcsicmp(text, L"All in") == 0 ? 7 : 0;
}

static void record_action(void *seatNode, int seat, int code)
{
    if (!code) return;
    AcquireSRWLockExclusive(&action_lock);
    for (int i = 0; i < MAX_TABLES; i++) {
        struct ActionTable *table = &action_tables[i];
        if (table->seats[seat] != seatNode) continue;
        BYTE *slot = slot_ptr(g_shared, i);
        if (*(HWND *)slot != table->hwnd || !IsWindow(table->hwnd)) continue;
        HWND host = *(HWND *)(g_shared + OFF_MAINHWND);
        if (!*(LONG *)(slot + ACTION_ACTIVE) || (host && !IsWindow(host))) continue;
        int cards = read_board_cards(table->board);
        if (cards < 0) cards = table->cards;
        if (cards < 0 || (table->last_seat == seat && table->last_code == code && table->last_cards == cards)) break;
        table->last_seat = seat;
        table->last_code = code;
        table->last_cards = cards;
        LONG sequence = *(LONG *)(slot + ACTION_HEAD) + 1;
        int index = (sequence - 1) % ACTION_CAPACITY;
        BYTE *entry = slot + ACTION_RING + index * 8;
        InterlockedExchange((volatile LONG *)entry, 0);
        *(LONG *)(entry + 4) = seat | (cards << 8) | (code << 16);
        *(DWORD *)(slot + ACTION_TICKS + index * sizeof(DWORD)) = GetTickCount();
        InterlockedExchange((volatile LONG *)entry, sequence);
        InterlockedExchange((volatile LONG *)(slot + ACTION_HEAD), sequence);
        break;
    }
    ReleaseSRWLockExclusive(&action_lock);
}

static int seat_for_action(void *node, int rootOnly, int mustBeVisible, void **seatNode)
{
    int action = 0;
    for (int depth = 0; node && depth < 12; depth++, node = action_parent(node)) {
        if (mustBeVisible && !p_isVisible(node)) return 0;
        wchar_t name[20] = {0};
        if (cc_str_read(p_getName(node), name, 19) && wcscmp(name, L"Action") == 0)
            action = 1;
        if (rootOnly && depth == 0 && !action) return 0;
        if (action && wcsncmp(name, L"Seat ", 5) == 0) {
            int seat = _wtoi(name + 5);
            *seatNode = node;
            return seat >= 1 && seat <= 10 ? seat : 0;
        }
    }
    return 0;
}

static void on_label_text(void *node, const void *value)
{
    wchar_t text[TEXT_MAXCHARS + 1];
    if (action_capture != 1 || !cc_str_read(value, text, TEXT_MAXCHARS)) return;
    int code = action_code(text);
    if (!code) return;
    InterlockedIncrement(&action_text_seen);
    void *seatNode;
    int seat = seat_for_action(node, 0, 0, &seatNode);
    if (seat) { InterlockedIncrement(&action_seat_seen); record_action(seatNode, seat, code); }
}

static void __fastcall proxy_text_hook(void *node, void *unused, const void *value)
{
    on_label_text(node, value);
    original_proxy_text(node, value);
}

static void __fastcall widget_text_hook(void *node, void *unused, const void *value)
{
    on_label_text(node, value);
    original_widget_text(node, value);
}

static int visible_action(void *node, int depth)
{
    if (!p_isVisible(node) || depth > 4) return 0;
    wchar_t text[TEXT_MAXCHARS + 1];
    int isWidget = 0;
    if (read_label(node, text, &isWidget)) {
        int code = action_code(text);
        if (code) return code;
    }
    void **children = p_getChildren(node);
    if (!children || IsBadReadPtr(children, 2 * sizeof(*children))) return 0;
    void **first = children[0], **last = children[1];
    if (!first || !last || last < first || last - first > 32) return 0;
    for (void **child = first; child < last; child++) {
        if (IsBadReadPtr(child, sizeof(*child))) break;
        int code = *child ? visible_action(*child, depth + 1) : 0;
        if (code) return code;
    }
    return 0;
}

static void __fastcall visible_hook(void *node, void *unused, unsigned char visible)
{
    int changed = p_isVisible(node) != visible;
    original_visible(node, visible);
    if (action_capture != 1 || !changed || !visible) return;
    void *seatNode;
    int seat = seat_for_action(node, 1, visible != 0, &seatNode);
    if (seat) { InterlockedIncrement(&action_root_seen); record_action(seatNode, seat, visible_action(node, 0)); }
}

static void start_action_capture(enum ActionHookMode mode)
{
    if (action_capture) return;
    action_capture = -1;
    HMODULE cocos = find_cocos_module();
    if (!cocos || !p_isVisible) return;
    action_parent = (cc_node_getter)GetProcAddress(cocos, "?getParent@Node@cocos2d@@UBEPBV12@XZ");
    if (!action_parent || MH_Initialize() != MH_OK) return;
    const unsigned proxy_mask = 1u << 1;
    const unsigned widget_mask = 1u << 2;
    const unsigned visible_mask = 1u << 3;
    const unsigned text_mask = proxy_mask | widget_mask;
    struct Hook { const char *name; void *callback; void **original; unsigned mask; } hooks[] = {
        {"?setString@LabelProxy@cocos2d@@QAEXABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@@Z", proxy_text_hook, (void **)&original_proxy_text, proxy_mask},
        {"?setString@Text@ui@cocos2d@@QAEXABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@@Z", widget_text_hook, (void **)&original_widget_text, widget_mask},
        {"?setVisible@Node@cocos2d@@UAEX_N@Z", visible_hook, (void **)&original_visible, visible_mask}
    };
    unsigned requested = text_mask | visible_mask;
    if (mode == ACTION_SKIP_VISIBLE) requested = text_mask;
    else if (mode == ACTION_PROXY_ONLY) requested = proxy_mask;
    else if (mode == ACTION_WIDGET_ONLY) requested = widget_mask;

    unsigned queued = 0;
    for (int i = 0; i < sizeof(hooks) / sizeof(*hooks); i++) {
        const unsigned hook_mask = hooks[i].mask;
        if (!(requested & hook_mask)) continue;
        void *target = (void *)GetProcAddress(cocos, hooks[i].name);
        if (target && MH_CreateHook(target, hooks[i].callback, hooks[i].original) == MH_OK &&
            MH_QueueEnableHook(target) == MH_OK) queued |= hook_mask;
    }
    HMODULE self;
    /* Callbacks must remain valid even if the host exits without removing its window hook. */
    if ((queued & visible_mask) == (requested & visible_mask) && (queued & text_mask) &&
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            (LPCWSTR)&start_action_capture, &self) && MH_ApplyQueued() == MH_OK) {
        action_hooks_status = queued;
        action_capture = 1;
    }
}
#endif

/* Enumera i figli del tavolo e registra gli HWND dei controlli
   nella mappa posti (slot+0x2668, 10 posti x 8 dword; dword 0 = HWND). */
struct ScanCtx { BYTE *slot; int seats; };

static BOOL CALLBACK seat_cb(HWND hwnd, LPARAM lp)
{
    struct ScanCtx *ctx = (struct ScanCtx *)lp;
    char cls[64] = {0};
    if (GetClassNameA(hwnd, cls, sizeof(cls)) <= 0) return TRUE;
    if (strncmp(cls, "PokerStarsButtonClass", 21) == 0 && ctx->seats < 10) {
        DWORD *seat = (DWORD *)(ctx->slot + 0x2668 + (SIZE_T)ctx->seats * 32);
        seat[0] = (DWORD)(UINT_PTR)hwnd;
        ctx->seats++;
    }
    return TRUE;
}

/* Legge il titolo dei controlli Win32 figli. */
static BOOL CALLBACK text_cb(HWND hwnd, LPARAM lp)
{
    struct TextCtx *ctx = (struct TextCtx *)lp;
    if (ctx->count >= TEXT_MAX) return TRUE;
    wchar_t buf[TEXT_MAXCHARS + 1] = {0};
    int len = GetWindowTextW(hwnd, buf, TEXT_MAXCHARS + 1);
    if (len <= 0) return TRUE;

    RECT cr = {0, 0, 0, 0};
    GetClientRect(hwnd, &cr);
    POINT tl = {0, 0};
    MapWindowPoints(hwnd, ctx->table, &tl, 1);

    BYTE *e = ctx->slot + TEXT_BASE + (SIZE_T)ctx->count * TEXT_ENTRY;
    *(DWORD *)(e + 0)  = (DWORD)(UINT_PTR)hwnd;
    *(DWORD *)(e + 4)  = (DWORD)tl.x;
    *(DWORD *)(e + 8)  = (DWORD)tl.y;
    *(DWORD *)(e + 12) = (DWORD)(cr.right - cr.left);
    *(DWORD *)(e + 16) = (DWORD)(cr.bottom - cr.top);
    *(DWORD *)(e + 20) = (DWORD)len;
    for (int i = 0; i < len; i++)
        *((wchar_t *)(e + 24) + i) = buf[i];
    *(DWORD *)(e + 24 + TEXT_MAXCHARS * 2) = 0;   /* nessun nome nodo */
    *(DWORD *)(e + 156) = 0;
    ctx->count++;
    return TRUE;
}

static void scan_table(BYTE *sh, int idx, LONG request)
{
    BYTE *slot = slot_ptr(sh, idx);
    if (!slot) return;
    HWND table = *(HWND *)slot;
    if (!table || GetWindowThreadProcessId(table, NULL) != GetCurrentThreadId()) return;
    InterlockedExchange((volatile LONG *)(slot + 0x19DC), 0);
    if (slot[OPTION_TRACE_ENABLE]) {
        cocos_bind();
        enum CocosScanStatus status = g_bind_status;
        if (g_cocos_bound) status = SCAN_EXPORTS_MISSING;
        struct TextCtx trace = {0};
        if (g_cocos_bound && g_can_find_view && p_isVisible && p_getName) {
            void *director = p_getInstance();
            status = SCAN_DIRECTOR_MISSING;
            void *scene = director ? table_scene(director, table, &status) : NULL;
            if (scene) {
                trace.optionTrace = OPTION_TRACE_WALKED;
                walk_node(scene, 0, &trace, 0, TABLE_OTHER, 0);
                status = SCAN_WALKED;
            }
        }
        *(DWORD *)(slot + OPTION_TRACE_RESULT) = trace.optionTrace;
        InterlockedExchange((volatile LONG *)(slot + 0x19EC), status);
        InterlockedExchange((volatile LONG *)(slot + 0x19DC), request);
        return;
    }
    struct ScanCtx ctx = { slot, 0 };
    EnumChildWindows(table, seat_cb, (LPARAM)&ctx);

    /* testi: walker cocos2d (scene graph) se bindato, altrimenti
       finestre figlie con titolo (client con controlli reali) */
    RECT rc = {0, 0, 0, 0};
    int cw = 0, chh = 0;
    if (GetClientRect(table, &rc)) { cw = rc.right - rc.left; chh = rc.bottom - rc.top; }
    struct TextCtx tctx = { .slot = slot, .table = table, .clientW = cw, .clientH = chh, .boardCards = -1 };
    wchar_t title[512] = {0};
    GetWindowTextW(table, title, ARRAYSIZE(title));
    const wchar_t *login = wcsstr(title, L"Logged In as ");
    if (login && login[13]) tctx.heroNick = login + 13;
    *(DWORD *)(slot + TEXT_COUNT) = 0;

    cocos_bind();
    enum CocosScanStatus status = g_bind_status;
    *(DWORD *)(slot + 0x19E4) = (p_getGLView ? 1u : 0u) |
                               (p_getNumGLViews ? 2u : 0u) |
                               (p_getWin32Window ? 4u : 0u);
    *(DWORD *)(slot + 0x19E8) = g_cocos_bound ? 1 : 0;   /* diag: bind cocos ok */
    if (g_cocos_bound) {
        void *director = p_getInstance();
        status = director ? SCAN_SCENE_MISSING : SCAN_DIRECTOR_MISSING;
        void *scene = NULL;
        if (director)
            scene = g_can_find_view ? table_scene(director, table, &status) : p_getRunningScene(director);
        if (scene) {
            if (p_getContentSize && cw > 0 && chh > 0) {
                const float *size = p_getContentSize(scene);
                if (size && !IsBadReadPtr(size, 2 * sizeof(float)) &&
                    size[0] >= 100 && size[0] <= 10000 && size[1] >= 100 && size[1] <= 10000) {
                    tctx.scaleX = cw / size[0];
                    tctx.scaleY = chh / size[1];
                }
            }
            walk_node(scene, 0, &tctx, 0, TABLE_OTHER, 0);
            status = SCAN_WALKED;
#if defined(__i386__)
            if (tctx.board) {
                AcquireSRWLockExclusive(&action_lock);
                struct ActionTable *capture = &action_tables[idx];
                if (capture->hwnd != table || capture->scene != scene)
                    memset(capture, 0, sizeof(*capture));
                capture->hwnd = table;
                capture->scene = scene;
                capture->board = tctx.board;
                for (int seat = 1; seat <= 10; seat++)
                    if (tctx.seats[seat]) capture->seats[seat] = tctx.seats[seat];
                capture->cards = tctx.boardCards;
                ReleaseSRWLockExclusive(&action_lock);
                const enum ActionHookMode hook_mode = slot[ACTION_HOOK_MODE];
                if (hook_mode != ACTION_SKIP_ALL)
                    start_action_capture(hook_mode);
                *(DWORD *)(slot + ACTION_HOOKS_STATUS) = action_hooks_status;
                *(LONG *)(slot + ACTION_TICKS_VERSION) = 1;
                *(LONG *)(slot + ACTION_ACTIVE) = (action_capture == 1);
                *(LONG *)(slot + 0x2540) = action_text_seen;
                *(LONG *)(slot + 0x2544) = action_root_seen;
                *(LONG *)(slot + 0x2548) = action_seat_seen;
            }
#endif
        }
    }
    if (tctx.count == 0)
        EnumChildWindows(table, text_cb, (LPARAM)&tctx);
    *(DWORD *)(slot + TEXT_COUNT) = (DWORD)tctx.count;
    *(LONG *)(slot + 0x19D8) = tctx.boardCards;
    *(LONG *)(slot + BETSCRIPT_HERO_SEAT) = tctx.heroSeat;
    *(LONG *)(slot + BETSCRIPT_AMOUNT) = tctx.canSetAmount;
#if defined(__i386__)
    AcquireSRWLockShared(&action_lock);
#endif
    *(LONG *)(slot + ACTION_SNAPSHOT_HEAD) = *(LONG *)(slot + ACTION_HEAD);
    *(DWORD *)(slot + ACTION_SNAPSHOT_TICK) = GetTickCount();
#if defined(__i386__)
    ReleaseSRWLockShared(&action_lock);
#endif
    *(DWORD *)(slot + 0x19F0) = (DWORD)tctx.nodes;   /* diag: nodi visitati */
    *(DWORD *)(slot + 0x19F4) = (DWORD)tctx.count;   /* diag: label raccolte */
    *(DWORD *)(slot + OPTION_TRACE_RESULT) = tctx.optionTrace;

    /* Segnala la scansione completata. */
    slot[0x15C] = 1;
    InterlockedExchange((volatile LONG *)(slot + 0x19EC), status);
    InterlockedExchange((volatile LONG *)(slot + 0x19DC), request);
}

/* Esegue il comando registrato nell'header condiviso. */
static void dispatch(BYTE *sh)
{
    int cmd = *(int *)(sh + OFF_CMD);
    int idx = *(int *)(sh + OFF_IDX);
    HWND main = *(HWND *)(sh + OFF_MAINHWND);

    g_dispatch++;

    switch (cmd) {
    case 0x74: /* Risponde al ping della finestra principale. */
        if (main && IsWindow(main))
        {
            SendMessageW(main, 0x402, (WPARAM)0x65, (LPARAM)0x42);
            g_replies++;
        }
        break;
    case 0x75:
    case 0x77:
    case 0x78:
    case 0x79:
        if (idx < 0 || idx >= MAX_TABLES) return;
        scan_table(sh, idx, 0);
        break;
    default:
        break;
    }
}

/* Export principale: callback WH_CALLWNDPROC.
   lParam = CWPSTRUCT { lParam, wParam, message, hwnd }. */
LRESULT CALLBACK hookProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    BYTE *sh = shared_attach();
    if (nCode == 0)
    {
        g_calls++;
        CWPSTRUCT *cwp = (CWPSTRUCT *)lParam;
        if (cwp) g_last_msg = (LONG)cwp->message;
    }
    if (sh && nCode == 0) {
        CWPSTRUCT *cwp = (CWPSTRUCT *)lParam;
        if (cwp->message == MSG_CUSTOM_DISPATCH) {
            if (InterlockedCompareExchange(&g_busy, 1, 0) == 0) {
                dispatch(sh);
                InterlockedExchange(&g_busy, 0);
            }
        }
        else if (cwp->message == 0x47) {
            HWND main = *(HWND *)(sh + OFF_MAINHWND);
            if (main && IsWindow(main))
                PostMessageW(main, MSG_NOTIFY, (WPARAM)0x47, (LPARAM)cwp->hwnd);
        }
    }
    return CallNextHookEx(NULL, nCode, wParam, lParam);
}

/* Utility per il main exe: verifica che la DLL sia caricata e il map aperto. */
int hookAttachShared(void)
{
    return shared_attach() != NULL;
}

/* Diagnostica: contatori della hook (chiamate / dispatch / shared ok). */
int hookCalls(void)    { return g_calls; }
int hookDispatches(void){ return g_dispatch; }
int hookSharedOk(void) { return g_shared_ok; }
int hookLastMsg(void)  { return g_last_msg; }
int hookMsgCalls(void) { return g_msg_calls; }
int hookReplies(void)  { return g_replies; }

/* WH_GETMESSAGE riceve i comandi quando il thread li estrae dalla coda. */
LRESULT CALLBACK hookGetMsgProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0) {
        g_msg_calls++;
        MSG *m = (MSG *)lParam;
        if (m && (m->message == MSG_CUSTOM_DISPATCH || m->message == MSG_TABLE_SCAN)) {
            BYTE *sh = shared_attach();
            if (sh && InterlockedCompareExchange(&g_busy, 1, 0) == 0) {
                if (m->message == MSG_TABLE_SCAN && wParam == PM_REMOVE)
                    scan_table(sh, (int)m->wParam, (LONG)m->lParam);
                else if (m->message == MSG_CUSTOM_DISPATCH)
                    dispatch(sh);
                InterlockedExchange(&g_busy, 0);
            }
        }
    }
    return CallNextHookEx(NULL, nCode, wParam, lParam);
}
