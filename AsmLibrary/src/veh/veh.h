/*
 * veh/veh.h - VEH 保护模块接口
 */
#ifndef ASMHOST_VEH_H
#define ASMHOST_VEH_H

#include "core/types.h"

/* 安装/卸载链首 VEH（幂等）。返回: 1=新装 0=已装 -1=失败 */
int   asmhost_veh_install(void);
void  asmhost_veh_remove(void);

/* 执行线程的 TLS 作业绑定（VEH handler 通过它定位当前 job） */
void  asmhost_tls_set(AsmExecJob *job);
AsmExecJob *asmhost_tls_get(void);

#endif /* ASMHOST_VEH_H */
