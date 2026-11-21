/*
 * TargetCpp - C++ 目标对象（控制台迷你游戏）
 *
 * 启动后:
 *   1. 加载 AsmLibrary.dll（运行目录，x86/x64 同名分目录），安装 VEH，
 *      注册游戏符号表，启动管道服务
 *   2. 打印全部可被主程序远程调用的函数/数据地址
 *   3. 进入游戏循环（血量/得分缓慢变化），支持控制台命令 list/hp/score/exit
 *
 * /nodll 参数: 不加载 AsmLibrary（用于测试主程序的 RemoteThread 兜底模式）
 */
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <windows.h>

/* ---- AsmLibrary 动态加载 ---- */
typedef int  (*fn_version)(void);
typedef int  (*fn_arch)(void);
typedef int  (*fn_install_veh)(void);
typedef int  (*fn_reg_sym)(const char *, void *);
typedef int  (*fn_pipe_start)(const char *);

static fn_version    asm_version;
static fn_arch       asm_arch;
static fn_install_veh asm_install;
static fn_reg_sym    asm_reg;
static fn_pipe_start asm_pipe_start;
static bool g_asm_ok = false;

/* ---- 游戏状态（公开数据槽） ---- */
static int g_score = 0;
static int g_hp = 100;
static int g_pos[3] = { 10, 20, 30 };

/* ---- 公开函数（游戏风格 API，主程序通过地址远程调用） ---- */
extern "C" {

__declspec(dllexport) int __cdecl GameAddScore(int points)
{
    g_score += points;
    if (g_score < 0) g_score = 0;
    return g_score;
}

__declspec(dllexport) int __cdecl GameGetScore(void)
{
    return g_score;
}

__declspec(dllexport) int __cdecl GameHeal(int amount)
{
    g_hp += amount;
    if (g_hp > 100) g_hp = 100;
    return g_hp;
}

__declspec(dllexport) int __cdecl GameGetHealth(void)
{
    return g_hp;
}

/* 伤害: 越界参数被钳制，用于测试参数封送 */
__declspec(dllexport) int __cdecl GameDamage(int amount)
{
    g_hp -= amount;
    if (g_hp < 0) g_hp = 0;
    return g_hp;
}

/* 写出位置到调用者提供的缓冲（测试指针参数） */
__declspec(dllexport) int __cdecl GameGetPos(int *out3)
{
    if (out3 == NULL) return -1;
    out3[0] = g_pos[0];
    out3[1] = g_pos[1];
    out3[2] = g_pos[2];
    return 0;
}

/* 慢函数: 约 2 秒 busy loop，供补丁演示（打补丁后瞬间返回） */
__declspec(dllexport) int __cdecl GameSlowCall(void)
{
    volatile int s = 0;
    for (volatile int i = 0; i < 150000000; i++) s += i;
    return s;
}

/* 崩溃演示: 空指针解引用 → AsmLibrary VEH 捕获，目标进程不崩溃 */
__declspec(dllexport) int __cdecl GameCrashNow(void)
{
    volatile int *p = (volatile int *)0;
    return *p;
}

} /* extern "C" */

static void print_symbols(void)
{
    struct { const char *name; void *addr; } syms[] = {
        { "GameAddScore",   (void *)GameAddScore },
        { "GameGetScore",   (void *)GameGetScore },
        { "GameHeal",       (void *)GameHeal },
        { "GameGetHealth",  (void *)GameGetHealth },
        { "GameDamage",     (void *)GameDamage },
        { "GameGetPos",     (void *)GameGetPos },
        { "GameSlowCall",   (void *)GameSlowCall },
        { "GameCrashNow",   (void *)GameCrashNow },
        { "VirtualProtect", (void *)VirtualProtect },
        { "g_score",        (void *)&g_score },
        { "g_hp",           (void *)&g_hp },
        { "g_pos",          (void *)g_pos },
    };
    printf("---- 符号表 (共 %d 项) ----\n", (int)(sizeof(syms) / sizeof(syms[0])));
    for (int i = 0; i < (int)(sizeof(syms) / sizeof(syms[0])); i++)
    {
        printf("  %-14s = 0x%p\n", syms[i].name, syms[i].addr);
        if (g_asm_ok)
            asm_reg(syms[i].name, syms[i].addr);
    }
}

static bool load_asmhost(void)
{
    /* x86/x64 同名分目录: 运行目录下的 AsmLibrary.dll 即本进程架构版本 */
    const char *dll = "AsmLibrary.dll";
    HMODULE h = LoadLibraryA(dll);
    if (h == NULL)
    {
        printf("[!] 加载 %s 失败 (GetLastError=%lu) —— 仅支持远程线程兜底模式\n",
               dll, GetLastError());
        return false;
    }
    asm_version    = (fn_version)GetProcAddress(h, "asmhost_version");
    asm_arch       = (fn_arch)GetProcAddress(h, "asmhost_arch");
    asm_install    = (fn_install_veh)GetProcAddress(h, "asmhost_install_veh");
    asm_reg        = (fn_reg_sym)GetProcAddress(h, "asmhost_register_symbol");
    asm_pipe_start = (fn_pipe_start)GetProcAddress(h, "asmhost_start_pipe_server");
    if (!asm_version || !asm_arch || !asm_install || !asm_reg || !asm_pipe_start)
    {
        printf("[!] AsmLibrary 导出不完整\n");
        return false;
    }
    asm_install();
    g_asm_ok = true;
    printf("[+] AsmLibrary 已加载: version=%x arch=%d\n", asm_version(), asm_arch());
    return true;
}

int main(int argc, char **argv)
{
    bool nodll = false;
    for (int i = 1; i < argc; i++)
        if (_stricmp(argv[i], "/nodll") == 0) nodll = true;

    printf("=== TargetCpp 迷你游戏目标 (arch=%s) ===\n", sizeof(void *) == 8 ? "x64" : "x86");

    if (!nodll && load_asmhost())
    {
        print_symbols();
        if (asm_pipe_start(NULL))
            printf("[+] 管道服务已启动: \\\\.\\pipe\\asmexec_%lu\n",
                   (unsigned long)GetCurrentProcessId());
    }
    else
    {
        print_symbols();
        printf("[i] 未加载 AsmLibrary —— 主程序可用 RemoteThread 兜底模式\n");
    }

    printf("[i] 命令: list=重印符号  hp=血量  score=得分  exit=退出\n");
    printf("[i] 游戏循环中，血量每秒 -1（可被远程 GameHeal 恢复）...\n\n");
    fflush(stdout);

    char line[128];
    DWORD last_tick = GetTickCount();
    int cnt = 0;
    HANDLE stdin_h = GetStdHandle(STD_INPUT_HANDLE);
    DWORD console_mode = 0;
    bool stdin_is_console = GetConsoleMode(stdin_h, &console_mode) != 0;

    for (;;)
    {
        /* 控制台: 有按键才读（不阻塞游戏循环）；重定向: 阻塞读 */
        bool has_input = false;
        if (stdin_is_console)
        {
            DWORD avail = 0;
            if (GetNumberOfConsoleInputEvents(stdin_h, &avail) && avail > 0)
                has_input = true;
        }
        else
        {
            has_input = true;   /* 阻塞 fgets */
        }

        if (has_input)
        {
            if (fgets(line, sizeof(line), stdin) == NULL)
            {
                /* EOF → 保持运行（供自动化测试/无人值守；退出走 exit 命令或外部终止） */
                Sleep(200);
                continue;
            }
            size_t l = strlen(line);
            while (l > 0 && (line[l - 1] == '\n' || line[l - 1] == '\r' || line[l - 1] == ' ' || line[l - 1] == '\t')) line[--l] = '\0';
            if (_stricmp(line, "exit") == 0) break;
            if (_stricmp(line, "list") == 0) print_symbols();
            else if (_stricmp(line, "hp") == 0)
                printf("g_hp = %d @ 0x%p\n", g_hp, (void *)&g_hp);
            else if (_stricmp(line, "score") == 0)
                printf("g_score = %d @ 0x%p\n", g_score, (void *)&g_score);
            else if (l > 0)
                printf("[?] 未知命令: %s\n", line);
            printf("> ");
            fflush(stdout);
        }

        /* 游戏循环: 每秒 hp-1, 每 3 秒 score+1 */
        DWORD now = GetTickCount();
        if (now - last_tick >= 1000)
        {
            last_tick = now;
            if (g_hp > 0) g_hp--;
            if (++cnt % 3 == 0) g_score += 1;
            printf("\r[游戏] HP=%d  Score=%d  ", g_hp, g_score);
            fflush(stdout);
        }
        Sleep(50);
    }

    printf("\n退出。\n");
    return 0;
}
