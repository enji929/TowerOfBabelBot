using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TowerOfBabelBot;

static class NativeHook
{
    const long FixedUpdateOffset = 0x4F2DC0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void PlayerFixedUpdateFn(IntPtr instance, IntPtr methodInfo);

    static PlayerFixedUpdateFn _original;
    static NativeDetour _detour;
    static PlayerFixedUpdateFn _hookDelegate;

    static IntPtr       _playerPtr = IntPtr.Zero;
    static Player       _playerObj = null;
    static Rigidbody2D  _playerRb  = null;

    public static void InvalidatePlayerCache()
    {
        _playerPtr = IntPtr.Zero;
        _playerObj = null;
        _playerRb  = null;
    }

    public static void Apply()
    {
        try
        {
            var module = Process.GetCurrentProcess().Modules
                .Cast<ProcessModule>()
                .FirstOrDefault(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));

            if (module == null) { Plugin.Log.LogError("GameAssembly.dll not found"); return; }

            var nativePtr = new IntPtr(module.BaseAddress.ToInt64() + FixedUpdateOffset);
            _hookDelegate = Hooked;
            _detour = new NativeDetour(nativePtr, Marshal.GetFunctionPointerForDelegate(_hookDelegate));
            _original = _detour.GenerateTrampoline<PlayerFixedUpdateFn>();
            Plugin.Log.LogInfo($"Hooked Player.FixedUpdate at 0x{nativePtr.ToInt64():X}");
        }
        catch (Exception e) { Plugin.Log.LogError($"Hook failed: {e}"); }
    }

    static IntPtr ResolvePlayerPtr()
    {
        if (_playerPtr != IntPtr.Zero) return _playerPtr;
        try { var p = Object.FindObjectOfType<Player>(); if (p != null) _playerPtr = p.Pointer; } catch { }
        return _playerPtr;
    }

    static void Hooked(IntPtr instance, IntPtr methodInfo)
    {
        if (instance == IntPtr.Zero) { _original(instance, methodInfo); return; }

        // Всегда пытаемся узнать реальный указатель Player, не только когда бот включён —
        // нужно для наблюдения за скоростью пока бот выключен.
        IntPtr playerPtr = ResolvePlayerPtr();
        bool isPlayer = playerPtr != IntPtr.Zero && instance == playerPtr;

        Rigidbody2D rb = null;
        Vector2 posBefore = Vector2.zero;

        if (isPlayer)
        {
            try
            {
                if (_playerObj == null) _playerObj = new Player(instance);
                if (_playerRb  == null) _playerRb  = _playerObj.rb;
                rb = _playerRb;
                if (rb != null) posBefore = rb.position;
                // velocity НЕ трогаем до _original: игра читает его внутри своего
                // FixedUpdate чтобы переключить анимацию idle → walk.
            }
            catch { }
        }

        _original(instance, methodInfo);

        if (!isPlayer || rb == null) return;

        try
        {
            if (!BotController.BotEnabled)
            {
                // Бот выключен — наблюдаем реальную скорость из дельты позиции.
                // Это самый точный способ: в юнити-единицах, со всеми бонусами от лута и скиллов.
                float moved = (rb.position - posBefore).magnitude;
                float dt = Time.fixedDeltaTime;
                if (moved > 0.001f && dt > 0f)
                    BotController.UpdateObservedSpeed(moved / dt);
                return;
            }

            Vector2 vel = BotController.CalcMoveVelocity(_playerObj);

            // MovePosition задаёт позицию (работает для kinematic и dynamic).
            // rb.velocity = vel — только для аниматора: он читает velocity чтобы
            // переключать idle → walk. Для кинематического тела velocity не двигает
            // его физикой, поэтому двойного движения нет.
            rb.MovePosition(posBefore + vel * Time.fixedDeltaTime);
            rb.velocity = vel;
        }
        catch { }
    }
}
