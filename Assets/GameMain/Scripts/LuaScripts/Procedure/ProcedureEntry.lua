---
--- Generated by EmmyLua(https://github.com/EmmyLua)
--- Created by 20200506QASD.
--- DateTime: 2022/1/12 10:06
---
---Lua流程进入  初始化一些东西

ProcedureEntry = {}

local this = ProcedureEntry

this.OnEnter = nil
this.OnUpdate = nil
this.OnLeave = nil

function ProcedureEntry.OnEnter(self)
   print("ProcedureEntry 流程进入")
end

local waitTime = 0
function ProcedureEntry.OnUpdate(self,elapseSeconds,realElapseSeconds)

   --进入这个流程的时候 应该等所有的lua 脚本require一下之后 继续在跑的
   self:ChangeProcedureLua("Procedure/ProcedureLogin","ProcedureLogin")
end

function ProcedureEntry.OnLeave(self)
   print("ProcedureEntry 流程退出")
end