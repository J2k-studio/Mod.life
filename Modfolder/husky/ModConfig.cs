namespace HuskyPal
{
    public class ModConfig
    {
        public int JumpMinTiles { get; set; } = 3;
        public int JumpMaxTiles { get; set; } = 6;
        /// <summary>Damage comes in multiples of this (3 -> 3, 6, 9).</summary>
        public int DamageStep { get; set; } = 3;
        public int MaxDamageTier { get; set; } = 3;
        /// <summary>Every N combat levels unlock one more damage tier (lvl 0-3: only 3, 4-7: 3 or 6, 8+: 3/6/9).</summary>
        public int CombatLevelsPerTier { get; set; } = 4;
        public float LeapCooldownSeconds { get; set; } = 1.5f;
        public int MonsterSearchTiles { get; set; } = 12;
        public bool JoinCavesAutomatically { get; set; } = true;
        public int GuardBarkRadiusTiles { get; set; } = 6;
    }
}
