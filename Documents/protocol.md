# AsmLibrary 管道协议 v1

主程序（或任何第三方客户端）通过命名管道与目标进程内的 AsmLibrary.dll 通信。

- 管道名：`\\.\pipe\asmexec_<pid>`（目标进程启动 AsmLibrary 后创建，单实例单客户端）
- 传输：字节流，**文本行协议**，每条命令/响应以 `\n` 结尾（兼容 CRLF）
- 编码：UTF-8（无 BOM）
- 连接建立后服务端先发一行 HELLO 应答

## 连接握手

服务端在客户端连上后主动发送：

```
OK proto=1 arch=<4|8> pid=<pid> version=<n> gate=<hex>
```

- `arch`：目标位数（4 = x86，8 = x64）
- `gate`：ret 重定向网关地址（见 architecture.md）。客户端汇编用户代码时应把
  无操作数 `ret` 改写为"把 gate 写入 [rsp] 再 ret"，避免 naive 内联代码的
  ret 槽与其调用的 home space 冲突

## 命令

### HELLO

```
> HELLO
< OK proto=1 arch=8 pid=1234 version=256 gate=1a2b0000
```

### SYMBOLS

```
> SYMBOLS
< BEGIN
< SYM GameAddScore 7ff600001234
< SYM g_score 7ff6000abc00
< ...
< END count=11
```

（BEGIN/END 之间为一帧多行响应）

### EXEC

```
> EXEC mode=<code|call> [call=<hex>] [args=<v,v,...>] timeout=<ms> code_hex=<HHHH...>
```

- `mode=code`：执行 `code_hex` 机器码（可空）
- `mode=call`：先执行机器码（可空），再按目标 ABI 封送 args 并调用 `call` 地址
  - x64（MSVC ABI）：args 依次进入 RCX/RDX/R8/R9，第 5+ 个参数进栈
  - x86（cdecl）：args 从右向左压栈，调用方清栈
- `args`：十进制、`0x` 十六进制
- 响应（单行）：

```
< OK ret=<hex> regs=rax=..,rcx=..,...,r15=.. flags=<hex>
< ERR status=exc code=<NTSTATUS hex> addr=<hex> av=<0|1> rw=<0|1|8> regs=... flags=...
< ERR status=timeout
< ERR status=busy
< ERR status=badarg
< ERR status=internal rc=<n>
```

- `rw`：0=读违例 1=写违例 8=DEP 执行违例

### PING / SHUTDOWN

```
> PING
< PONG arch=8
> SHUTDOWN
< BYE            （服务停止）
```

## 嵌入式客户端示例（伪代码）

```python
pipe = open(rf"\\.\pipe\asmexec_{pid}")
hello = pipe.readline()          # OK proto=1 ...
gate = hello.gate
code = assemble("mov rax, GameAddScore; mov rcx, 5; call rax; ret",
                ret_trap=gate)   # ret → 写 gate 入 [rsp] 再 ret
pipe.write(f"EXEC mode=code timeout=3000 code_hex={code.hex()}\n")
print(pipe.readline())
```
