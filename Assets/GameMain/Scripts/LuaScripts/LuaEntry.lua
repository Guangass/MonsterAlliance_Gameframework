---
--- Generated by EmmyLua(https://github.com/EmmyLua)
--- Created by 20200506QASD.
--- DateTime: 2021/12/14 16:18
---
---Lua入口

LuaEntry = {}

--判断热更新

function LuaEntry.OnStart()
   
   require "LuaConfig"
   --加载热更新
end

function LuaEntry.OnUpdate(deltaTime, unscaledDeltaTime)
   print("进入Update:",deltaTime)
end

function LuaEntry.OnFixedUpdate(deltaTime, unscaledDeltaTime)
   print("进入OnFixedUpdate:",deltaTime)
end

function LuaEntry.OnDestroy()
    
end