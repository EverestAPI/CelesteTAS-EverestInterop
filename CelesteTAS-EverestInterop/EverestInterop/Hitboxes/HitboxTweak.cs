namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxTweak {
        public static void Load() {
            HitboxTriggerSpikes.Load();
            ActualEntityCollideHitbox.Load();
            HitboxFixer.Load();
            HitboxSimplified.Load();
            HitboxHideTrigger.Load();
            HitboxColor.Load();
            HitboxFinalBoss.Load();
            HitboxOptimized.Load();
        }

        public static void Unload() {
            HitboxTriggerSpikes.Unload();
            ActualEntityCollideHitbox.Unload();
            HitboxFixer.Unload();
            HitboxSimplified.Unload();
            HitboxHideTrigger.Unload();
            HitboxColor.Unload();
            HitboxFinalBoss.Unload();
            HitboxOptimized.Unload();
        }
    }
}