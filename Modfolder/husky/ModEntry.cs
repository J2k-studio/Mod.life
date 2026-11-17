using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Buildings;
using StardewValley.GameData.Pets;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Characters;

namespace HuskyPal
{
    public enum Mode { Free, Follow, Guard }
    enum Anim { None, Spin, Tilt, Bark, Play, Derp, Drink, Sit, Howl, Sleep, Leap }

    // Sprite sheet rows (4 frames each, 32x32). frame index = row*4 + col
    static class Row
    {
        public const int WalkDown = 0, RunDown = 1, WalkRight = 2, RunRight = 3, WalkUp = 4, RunUp = 5, WalkLeft = 6, RunLeft = 7,
            Sit = 8, SleepCurl = 9, SleepBack = 10, DrinkR = 11, DrinkL = 12, PlayR = 13, PlayL = 14, Spin = 15,
            BarkR = 16, BarkL = 17, LeapR = 18, LeapL = 19, Derp = 20, GuardLie = 21;
    }

    class HState
    {
        public Anim Anim = Anim.None;
        public int AnimTicks, AnimLen;
        public Vector2 LastPos;
        public int IdleTicks, NextDerpAt = 600, ThirstTicks, WetTicks;
        public int LeapCooldown, FollowRepath, BarkCooldown;
        // leap
        public Vector2 LeapFrom, LeapTo; public int LeapT;
        public bool Running;
        public bool GoingToDrink;
    }

    public class ModEntry : Mod
    {
        const string TypeId = "Jao.HuskyPal_Husky";
        const string TexKey = "Mods/Jao.HuskyPal/Husky";
        const string IconKey = "Mods/Jao.HuskyPal/HuskyIcon";
        const string ModeKey = "Jao.HuskyPal/mode";
        const string SpawnedKey = "spawned";

        ModConfig Config;
        readonly Dictionary<Pet, HState> States = new();
        readonly Random Rng = new();
        int tick;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => States.Clear();
            helper.Events.Player.Warped += OnWarped;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
        }

        // ---------- assets ----------
        void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(TexKey))
                e.LoadFromModFile<Texture2D>("assets/husky.png", AssetLoadPriority.Medium);
            else if (e.NameWithoutLocale.IsEquivalentTo(IconKey))
                e.LoadFromModFile<Texture2D>("assets/husky_icon.png", AssetLoadPriority.Medium);
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Pets"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, PetData>().Data;
                    if (!data.TryGetValue("Dog", out var dog)) return;
                    // clone the vanilla dog so sounds/behaviors stay valid, then swap the art
                    var husky = JsonConvert.DeserializeObject<PetData>(JsonConvert.SerializeObject(dog));
                    if (husky is null) return;
                    husky.DisplayName = "Husky";
                    husky.Breeds = new List<PetBreed>
                    {
                        new PetBreed
                        {
                            Id = "0", Texture = TexKey, IconTexture = IconKey,
                            IconSourceRect = new Rectangle(0, 0, 16, 16),
                            CanBeChosenAtStart = false, CanBeAdoptedFromMarnie = false
                        }
                    };
                    data[TypeId] = husky;
                });
            }
        }

        // ---------- spawn / naming ----------
        IEnumerable<Pet> Huskies() => Utility.getAllPets().Where(p => p.petType.Value == TypeId);

        void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer) return;
            bool spawned = Helper.Data.ReadSaveData<Dictionary<string, string>>("Jao.HuskyPal")?.ContainsKey(SpawnedKey) ?? false;
            if (!spawned && !Huskies().Any())
            {
                var house = Game1.getLocationFromName("FarmHouse") as FarmHouse;
                if (house == null) return;
                Point p = house.getEntryLocation();
                var pet = new Pet(p.X + 2, p.Y - 2, "0", TypeId) { currentLocation = house };
                pet.Name = pet.displayName = "Husky";
                pet.modData[ModeKey] = Mode.Free.ToString();
                house.characters.Add(pet);
                Helper.Data.WriteSaveData("Jao.HuskyPal", new Dictionary<string, string> { [SpawnedKey] = "1" });
                Game1.addHUDMessage(new HUDMessage("A husky wandered into your house!"));
                Game1.afterDialogues = () => OpenNaming(pet);
                if (Game1.activeClickableMenu == null) { var f = Game1.afterDialogues; Game1.afterDialogues = null; f(); }
            }
        }

        void OpenNaming(Pet pet)
        {
            Game1.activeClickableMenu = new NamingMenu(name =>
            {
                if (!string.IsNullOrWhiteSpace(name)) pet.Name = pet.displayName = name;
                Game1.exitActiveMenu();
            }, "Name your husky", pet.Name);
        }

        // ---------- helpers ----------
        Mode GetMode(Pet p) => Enum.TryParse(p.modData.TryGetValue(ModeKey, out var s) ? s : "Free", out Mode m) ? m : Mode.Free;
        void SetMode(Pet p, Mode m) => p.modData[ModeKey] = m.ToString();
        HState St(Pet p) { if (!States.TryGetValue(p, out var s)) States[p] = s = new HState { LastPos = p.Position }; return s; }
        static bool IsCave(GameLocation l) => l is MineShaft || l is VolcanoDungeon;

        void SetFrame(Pet p, int row, int col)
        {
            p.Sprite.StopAnimation();
            p.Sprite.CurrentFrame = row * 4 + col;
            p.Sprite.UpdateSourceRect();
        }

        void Play(Pet p, HState s, Anim a, int ticks) { s.Anim = a; s.AnimTicks = 0; s.AnimLen = ticks; p.controller = null; }

        void Say(string msg) => Game1.addHUDMessage(new HUDMessage(msg));

        // ---------- input: tap husky for a menu (phone friendly) ----------
        void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsPlayerFree || !e.Button.IsActionButton()) return;
            var loc = Game1.currentLocation;
            Pet hit = Huskies().FirstOrDefault(p => p.currentLocation == loc && Vector2.Distance(p.Tile, e.Cursor.GrabTile) < 1.6f
                                                    && Vector2.Distance(p.Tile, Game1.player.Tile) < 3.5f);
            if (hit == null) return;
            Helper.Input.Suppress(e.Button);
            var responses = new[]
            {
                new Response("pet", "Pet " + hit.Name),
                new Response("follow", "Follow me"),
                new Response("guard", "Guard the house"),
                new Response("free", "Roam free"),
                new Response("rename", "Rename"),
                new Response("cancel", "Cancel")
            };
            loc.createQuestionDialogue($"{hit.Name} ({GetMode(hit)})", responses, (who, key) =>
            {
                var s = St(hit);
                switch (key)
                {
                    case "pet": hit.checkAction(who, loc); break;
                    case "follow": SetMode(hit, Mode.Follow); Say(hit.Name + ": following you"); break;
                    case "guard": SetMode(hit, Mode.Guard); Say(hit.Name + ": guarding the house"); break;
                    case "free": SetMode(hit, Mode.Free); Say(hit.Name + ": roaming free"); break;
                    case "rename": OpenNaming(hit); break;
                }
            });
        }

        // ---------- warping ----------
        void OnWarped(object sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer) return;
            foreach (var pet in Huskies().ToList())
            {
                var m = GetMode(pet);
                bool join = m == Mode.Follow || (IsCave(e.NewLocation) && Config.JoinCavesAutomatically && m != Mode.Guard);
                if (join && pet.currentLocation != e.NewLocation)
                {
                    Game1.warpCharacter(pet, e.NewLocation, e.Player.Tile);
                    pet.controller = null;
                }
                else if (IsCave(e.OldLocation) && !IsCave(e.NewLocation) && m == Mode.Free)
                {
                    pet.warpToFarmHouse(Game1.player);
                }
            }
        }

        // ---------- main loop ----------
        void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            tick++;
            foreach (var pet in Huskies().ToList())
            {
                var s = St(pet);
                if (s.LeapCooldown > 0) s.LeapCooldown--;
                if (s.BarkCooldown > 0) s.BarkCooldown--;
                var mode = GetMode(pet);
                var player = Game1.player;
                var loc = pet.currentLocation;
                if (loc == null) continue;

                bool moved = Vector2.Distance(pet.Position, s.LastPos) > 0.3f;
                s.LastPos = pet.Position;

                // --- leap in progress ---
                if (s.Anim == Anim.Leap) { TickLeap(pet, s, loc); continue; }

                // --- combat (caves) ---
                if (IsCave(loc) && player.currentLocation == loc && s.LeapCooldown <= 0 && s.Anim == Anim.None)
                    if (TryStartLeap(pet, s, loc)) continue;

                // --- mode logic ---
                if (s.Anim == Anim.None)
                {
                    if (mode == Mode.Follow && player.currentLocation == loc) DoFollow(pet, s, player, loc);
                    else if (mode == Mode.Guard) DoGuard(pet, s, player);
                    else if (mode == Mode.Free) DoFree(pet, s, loc, moved);
                }

                // --- animation ---
                if (s.Anim != Anim.None) TickAnim(pet, s);
                else if (moved || pet.controller != null) WalkFrames(pet, s);
                else IdleFrames(pet, s, mode);
            }
        }

        // ---------- follow ----------
        void DoFollow(Pet pet, HState s, Farmer player, GameLocation loc)
        {
            float d = Vector2.Distance(pet.Tile, player.Tile);
            s.Running = d > 6f;
            if (d > 2.5f && --s.FollowRepath <= 0)
            {
                s.FollowRepath = 30;
                Point target = new Point((int)player.Tile.X, (int)player.Tile.Y + 1);
                pet.controller = new StardewValley.Pathfinding.PathFindController(pet, loc, target, -1);
            }
            else if (d <= 2.5f && pet.controller != null && Rng.Next(60) == 0) pet.controller = null;
            if (s.Running) pet.speed = 5; else pet.speed = 3;
            if (d > 25f) { Game1.warpCharacter(pet, loc, player.Tile); }
        }

        // ---------- guard ----------
        void DoGuard(Pet pet, HState s, Farmer player)
        {
            var farm = Game1.getFarm();
            Point door = farm.GetMainFarmHouseEntry();
            Vector2 spot = new Vector2(door.X + 1, door.Y + 1);
            if (pet.currentLocation != farm && pet.currentLocation is not FarmHouse)
            { Game1.warpCharacter(pet, farm, spot); return; }
            if (pet.currentLocation == farm)
            {
                if (Vector2.Distance(pet.Tile, spot) > 1.5f && pet.controller == null && --s.FollowRepath <= 0)
                {
                    s.FollowRepath = 120;
                    pet.controller = new StardewValley.Pathfinding.PathFindController(pet, farm, new Point((int)spot.X, (int)spot.Y), 2);
                }
                // bark at approaching farmer or crows
                bool crow = farm.critters != null && farm.critters.OfType<Crow>().Any(c => Vector2.Distance(c.position / 64f, pet.Tile) < 10);
                bool near = player.currentLocation == farm && Vector2.Distance(player.Tile, pet.Tile) < Config.GuardBarkRadiusTiles
                            && player.Tile != pet.Tile;
                if ((crow || (near && Game1.timeOfDay >= 2000)) && s.BarkCooldown <= 0 && pet.controller == null)
                {
                    s.BarkCooldown = 60 * 20;
                    Play(pet, s, Anim.Bark, 80);
                }
            }
        }

        // ---------- free roam + derp ----------
        void DoFree(Pet pet, HState s, GameLocation loc, bool moved)
        {
            // thirst
            s.ThirstTicks++;
            var farm = Game1.getFarm();
            if (s.ThirstTicks > 60 * 150 && !s.GoingToDrink && (loc == farm))
            {
                var bowl = farm.buildings.OfType<PetBowl>().FirstOrDefault();
                if (bowl != null)
                {
                    s.GoingToDrink = true;
                    pet.controller = new StardewValley.Pathfinding.PathFindController(pet, farm,
                        new Point(bowl.tileX.Value, bowl.tileY.Value + 1), 0);
                }
                else s.ThirstTicks = 0;
            }
            if (s.GoingToDrink)
            {
                var bowl = farm.buildings.OfType<PetBowl>().FirstOrDefault();
                if (bowl == null || loc != farm) { s.GoingToDrink = false; return; }
                if (Vector2.Distance(pet.Tile, new Vector2(bowl.tileX.Value + 0.5f, bowl.tileY.Value + 0.5f)) < 2f)
                {
                    s.GoingToDrink = false; s.ThirstTicks = 0; s.WetTicks = 150;
                    pet.FacingDirection = 1;
                    Play(pet, s, Anim.Drink, 180);
                }
                return;
            }
            // random derpiness
            s.IdleTicks++;
            if (!moved && pet.controller == null && s.IdleTicks > s.NextDerpAt)
            {
                s.IdleTicks = 0; s.NextDerpAt = 480 + Rng.Next(900);
                switch (Rng.Next(8))
                {
                    case 0: Play(pet, s, Anim.Spin, 160); break;      // chase own tail
                    case 1: Play(pet, s, Anim.Tilt, 100); break;      // confused head tilt
                    case 2: Play(pet, s, Anim.Bark, 80); break;       // "hao!"
                    case 3: Play(pet, s, Anim.Play, 120); break;      // play bow
                    case 4: Play(pet, s, Anim.Derp, 120); break;      // stretch / startled / bump
                    case 5: Play(pet, s, Anim.Howl, 140); break;      // dramatic howl
                    case 6: Play(pet, s, Anim.Sit, 200); break;
                    default: Play(pet, s, Anim.Sleep, 420); break;
                }
            }
        }

        // ---------- animations ----------
        int DirRow(Pet pet, bool run)
        {
            switch (pet.FacingDirection)
            {
                case 0: return run ? Row.RunUp : Row.WalkUp;
                case 1: return run ? Row.RunRight : Row.WalkRight;
                case 3: return run ? Row.RunLeft : Row.WalkLeft;
                default: return run ? Row.RunDown : Row.WalkDown;
            }
        }

        void WalkFrames(Pet pet, HState s)
        {
            bool run = s.Running || pet.speed >= 5;
            SetFrame(pet, DirRow(pet, run), (tick / (run ? 5 : 8)) % 4);
        }

        void IdleFrames(Pet pet, HState s, Mode mode)
        {
            if (mode == Mode.Guard) { SetFrame(pet, Row.GuardLie, (tick / 90) % 4 == 1 ? 1 : 0); return; }
            if (s.WetTicks > 0) { s.WetTicks--; SetFrame(pet, Row.Derp, 3); return; }
            int col = (tick / 40) % 2 == 0 ? 0 : 2;
            SetFrame(pet, Row.Sit, col == 0 ? 0 : 0);
        }

        void TickAnim(Pet pet, HState s)
        {
            s.AnimTicks++;
            int t = s.AnimTicks;
            bool right = pet.FacingDirection != 3;
            switch (s.Anim)
            {
                case Anim.Spin: SetFrame(pet, Row.Spin, (t / 6) % 4); break;
                case Anim.Tilt: SetFrame(pet, Row.Sit, (t / 20) % 2 == 0 ? 1 : 0); break;
                case Anim.Sit: SetFrame(pet, Row.Sit, (t / 45) % 2 == 0 ? 0 : 3); break;
                case Anim.Bark:
                    SetFrame(pet, right ? Row.BarkR : Row.BarkL, (t / 10) % 4);
                    if (t == 20 || t == 50) Game1.playSound("dog_bark");
                    break;
                case Anim.Howl:
                    SetFrame(pet, Row.Derp, 3); if (t == 10) Game1.playSound("dog_bark"); break;
                case Anim.Play: SetFrame(pet, right ? Row.PlayR : Row.PlayL, (t / 10) % 4); break;
                case Anim.Derp: SetFrame(pet, Row.Derp, (t / 30) % 3); break;
                case Anim.Drink: SetFrame(pet, right ? Row.DrinkR : Row.DrinkL, (t / 12) % 4); break;
                case Anim.Sleep: SetFrame(pet, (t / 150) % 2 == 0 ? Row.SleepCurl : Row.SleepBack, 2 + (t / 40) % 2); break;
            }
            if (t >= s.AnimLen)
            {
                if (s.Anim == Anim.Drink) pet.doEmote(20);
                s.Anim = Anim.None;
            }
        }

        // ---------- cave combat ----------
        int RollDamage()
        {
            int combat = Game1.player.CombatLevel;
            int maxTier = Math.Clamp(1 + combat / Math.Max(1, Config.CombatLevelsPerTier), 1, Math.Max(1, Config.MaxDamageTier));
            return Config.DamageStep * Rng.Next(1, maxTier + 1);
        }

        bool TryStartLeap(Pet pet, HState s, GameLocation loc)
        {
            Monster target = null; float best = Config.MonsterSearchTiles;
            foreach (var m in loc.characters.OfType<Monster>())
            {
                if (m.Health <= 0) continue;
                float d = Vector2.Distance(m.Tile, pet.Tile);
                if (d < best) { best = d; target = m; }
            }
            if (target == null) return false;
            Vector2 dir = target.Tile - pet.Tile;
            float dist = dir.Length();
            dir = dist > 0.01f ? dir / dist : new Vector2(1, 0);
            float jump = Math.Clamp(dist, Config.JumpMinTiles, Config.JumpMaxTiles);
            Vector2 landing = pet.Tile + dir * jump;
            // shorten if the landing tile is blocked
            while (jump > 1 && !loc.isTilePassable(new Vector2((int)Math.Round(landing.X), (int)Math.Round(landing.Y))))
            { jump -= 1; landing = pet.Tile + dir * jump; }
            s.Anim = Anim.Leap; s.LeapT = 0; pet.controller = null;
            s.LeapFrom = pet.Position;
            s.LeapTo = new Vector2((int)Math.Round(landing.X) * 64f, (int)Math.Round(landing.Y) * 64f);
            pet.FacingDirection = dir.X >= 0 ? 1 : 3;
            Game1.playSound("dog_bark");
            return true;
        }

        void TickLeap(Pet pet, HState s, GameLocation loc)
        {
            const int Len = 18;
            s.LeapT++;
            float t = Math.Min(1f, s.LeapT / (float)Len);
            pet.Position = Vector2.Lerp(s.LeapFrom, s.LeapTo, t);
            bool right = pet.FacingDirection != 3;
            SetFrame(pet, right ? Row.LeapR : Row.LeapL, Math.Min(3, s.LeapT * 4 / Len));
            if (s.LeapT >= Len)
            {
                int dmg = RollDamage();
                var area = new Rectangle((int)s.LeapTo.X - 64, (int)s.LeapTo.Y - 64, 192, 192); // 3x3 tiles around landing
                loc.damageMonster(area, dmg, dmg, false, Game1.player);
                Game1.playSound("hitEnemy");
                s.Anim = Anim.None;
                s.LeapCooldown = (int)(Config.LeapCooldownSeconds * 60);
            }
        }
    }
}
