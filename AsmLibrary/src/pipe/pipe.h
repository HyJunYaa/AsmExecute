/*
 * pipe/pipe.h - 命名管道服务模块接口
 */
#ifndef ASMHOST_PIPE_H
#define ASMHOST_PIPE_H

/* 启动管道服务（pipe_name: NULL → 默认 "\\.\pipe\asmexec_<pid>"）。
 * 返回: 1 = 已启动, 0 = 失败/已在运行 */
int  asmhost_pipe_start(const char *pipe_name);
void asmhost_pipe_stop(void);
int  asmhost_pipe_running(void);

#endif /* ASMHOST_PIPE_H */
