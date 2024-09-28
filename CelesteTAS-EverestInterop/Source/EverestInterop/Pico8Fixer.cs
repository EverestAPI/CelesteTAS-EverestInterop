using Monocle;
using TAS.Module;
using Emulator = Celeste.Pico8.Emulator;

namespace TAS.EverestInterop; 

public static class Pico8Fixer {
    // Set Pico8Fixer.Seed when need
    public static int Seed { get; private set; } = 0;
    public static int Frames { get; private set; } = 0;

    [Load]
    private static void Load() {
        On.Celeste.Pico8.Classic.balloon.init += BalloonOnInit;
        On.Celeste.Pico8.Classic.chest.update += ChestOnupdate;
        On.Celeste.Pico8.Emulator.ctor += EmulatorOnCtor;
        On.Celeste.Pico8.Classic.Init += ClassicOnInit;
        On.Celeste.Pico8.Classic.Update += ClassicOnUpdate;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Pico8.Classic.balloon.init -= BalloonOnInit;
        On.Celeste.Pico8.Classic.chest.update -= ChestOnupdate;
        On.Celeste.Pico8.Emulator.ctor -= EmulatorOnCtor;
        On.Celeste.Pico8.Classic.Init -= ClassicOnInit;
        On.Celeste.Pico8.Classic.Update -= ClassicOnUpdate;
    }

    private static void BalloonOnInit(On.Celeste.Pico8.Classic.balloon.orig_init orig, Celeste.Pico8.Classic.balloon self, Celeste.Pico8.Classic g, Emulator e) {
        if (Manager.Running) {
            Calc.PushRandom(g.level_index() + Seed);
        }

        orig(self, g, e);

        if (Manager.Running) {
            Calc.PopRandom();
        }
    }

    private static void ChestOnupdate(On.Celeste.Pico8.Classic.chest.orig_update orig, Celeste.Pico8.Classic.chest self) {
        bool running = self.G.has_key && Manager.Running;
        if (running) {
            Calc.PushRandom((int) self.timer + Seed);
        }

        orig(self);

        if (running) {
            Calc.PopRandom();
        }
    }

    private static void EmulatorOnCtor(On.Celeste.Pico8.Emulator.orig_ctor orig, Emulator self, Scene returnTo, int levelX, int levelY) {
        Seed = 0;
        orig(self, returnTo, levelX, levelY);
    }

    private static void ClassicOnInit(On.Celeste.Pico8.Classic.orig_Init orig, Celeste.Pico8.Classic self, Emulator emulator) {
        orig(self, emulator);
        if (Manager.Running) {
            int levelIndex = emulator.bootLevel.X % 8 + emulator.bootLevel.Y * 8;
            bool doubleJump = levelIndex is > 21 and < 31;
            self.max_djump = doubleJump ? 2 : 1;
            self.new_bg = doubleJump;
        }
    }

    private static void ClassicOnUpdate(On.Celeste.Pico8.Classic.orig_Update orig, Celeste.Pico8.Classic self) {
        orig(self);

        if (self.level_index() < 30) {
            Frames = self.frames;
        }
    }
}