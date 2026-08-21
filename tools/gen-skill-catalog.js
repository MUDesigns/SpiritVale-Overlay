const fs = require("fs");
const text = fs.readFileSync(
  "X:/projects/spirit-vale-tools--kar-mi-spirit-vale-tools-capture-1.7.0/packages/skills/src/definitions/skills.ts",
  "utf8"
);
const start = text.indexOf("static readonly values = [");
const end = text.lastIndexOf("] as const");
const body = text.slice(start, end);
const skills = [];
const re =
  /\{\s*"id":\s*"([^"]+)"\s*,\s*"displayName":\s*"([^"]+)"([\s\S]*?)(?=\n\s*\{|\n\s*\]\s*$)/g;
let m;
while ((m = re.exec(body))) {
  const id = m[1];
  const displayName = m[2];
  const rest = m[3];
  const sm = rest.match(/"spriteId":\s*"([^"]*)"/);
  skills.push({ id, displayName, spriteId: sm ? sm[1] : null });
}

// Class name → archetype id (matches overlay ArchetypeNames)
const ARCH = {
  Novice: -1,
  Warrior: 0,
  Mage: 1,
  Rogue: 2,
  Knight: 3,
  Summoner: 4,
  Acolyte: 5,
  Scout: 6,
  Paladin: 10,
  "Dragon Knight": 11,
  Berserker: 12,
  Revenant: 13,
  Priest: 14,
  Monk: 15,
  Wizard: 16,
  Chronomancer: 17,
  Druid: 18,
  Warlock: 19,
  Assassin: 20,
  Shinobi: 21,
  Gunslinger: 22,
  Ranger: 23,
  Jester: 24,
  Nightshade: 25,
  Necromancer: 26,
  Spellblade: 27,
  "Blade Master": 28,
  Mechanist: 29,
  Alchemist: 30,
  Weaver: 31,
};

const prefixToClass = {
  Warrior: "Warrior",
  Mage: "Mage",
  Rogue: "Rogue",
  Knight: "Knight",
  Summoner: "Summoner",
  Acolyte: "Acolyte",
  Scout: "Scout",
  Paladin: "Paladin",
  Berserker: "Berserker",
  Revenant: "Revenant",
  Priest: "Priest",
  Monk: "Monk",
  Wizard: "Wizard",
  Chronomancer: "Chronomancer",
  Druid: "Druid",
  Warlock: "Warlock",
  Assassin: "Assassin",
  Shinobi: "Shinobi",
  Gunslinger: "Gunslinger",
  Ranger: "Ranger",
  Jester: "Jester",
  Nightshade: "Nightshade",
  Necromancer: "Necromancer",
  Spellblade: "Spellblade",
  BladeMaster: "Blade Master",
  Mechanist: "Mechanist",
  Alchemist: "Alchemist",
  Weaver: "Weaver",
  DragonKnight: "Dragon Knight",
};

const spriteStemToClass = {
  Barbarian: "Warrior",
  Berserker: "Berserker",
  Paladin: "Paladin",
  Necromancer: "Necromancer",
  Rogue: "Rogue",
  Hunter: "Scout",
  Priest: "Priest",
  Geomancer: "Wizard",
  Pyromancer: "Wizard",
  Electromancer: "Wizard",
  Enchanter: "Mage",
};

// Curated skill id → class (wiki / common trees). Overrides weaker hints.
const curated = {
  // Warrior
  AxeMastery: "Warrior",
  Bash: "Warrior",
  CritMastery: "Warrior",
  DualWieldMastery: "Warrior",
  ResistanceMastery: "Warrior",
  AxeArc: "Warrior",
  AxeThrow: "Warrior",
  AxeVortex: "Warrior",
  Whirlwind: "Warrior",
  TwohandQuicken: "Warrior",
  WildCharge: "Berserker",
  Berserk: "Berserker",
  BloodFrenzy: "Berserker",
  BloodLust: "Berserker",
  Cyclone: "Berserker",
  Execute: "Berserker",
  GainRage: "Berserker",
  GroundSlam: "Berserker",
  RageMastery: "Berserker",
  Unyielding: "Berserker",
  ShoutBlood: "Berserker",
  ShoutFury: "Berserker",
  ShoutMight: "Berserker",
  // Mage / Wizard
  WandMastery: "Mage",
  EarthSpikes: "Mage",
  Fireball: "Mage",
  TrueSight: "Mage",
  ThunderStorm: "Mage",
  IceShard: "Mage",
  EnergyShield: "Mage",
  FreeCast: "Mage",
  Blink: "Mage",
  Firebolt: "Mage",
  Icebolt: "Mage",
  Meteor: "Wizard",
  TetraVortex: "Wizard",
  ChainLightning: "Wizard",
  Earthquake: "Wizard",
  FreezingField: "Wizard",
  Tempest: "Wizard",
  Thunderbolt: "Wizard",
  ThunderField: "Wizard",
  // Scout / Ranger / Gunslinger
  SteadyHands: "Scout",
  StrafingVolley: "Scout",
  PreciseAim: "Scout",
  ArrowShower: "Scout",
  ForceShot: "Scout",
  Marked: "Scout",
  Agility: "Scout",
  SlowTrap: "Scout",
  VolatileBolt: "Scout",
  AerialShot: "Scout",
  PiercingShot: "Scout",
  SniperShot: "Scout",
  SniperNest: "Scout",
  JumpShot: "Gunslinger",
  GunMastery: "Gunslinger",
  FanFire: "Gunslinger",
  PointBlankShot: "Gunslinger",
  TriggerHappy: "Gunslinger",
  ExplosiveGrenade: "Gunslinger",
  // Rogue / Assassin / Shinobi
  FanOfKnives: "Rogue",
  ShadowSeal: "Rogue",
  ShadowStep: "Rogue",
  ShadowStrike: "Rogue",
  VenomCoating: "Rogue",
  VenomStrike: "Rogue",
  Cloaking: "Rogue",
  SmokeScreen: "Rogue",
  // Knight / Paladin
  ShieldBash: "Knight",
  ShieldThrow: "Knight",
  ShieldMastery: "Knight",
  SpearMastery: "Knight",
  SpearStab: "Knight",
  SpearThrust: "Knight",
  SpearSlice: "Knight",
  Taunt: "Knight",
  HighGuard: "Knight",
  Bonk: "Paladin",
  HolyLight: "Paladin",
  HolyShield: "Paladin",
  HolyWrath: "Paladin",
  GrandCross: "Paladin",
  Consecration: "Paladin",
  Smite: "Paladin",
  // Acolyte / Priest
  Heal: "Acolyte",
  HealAll: "Acolyte",
  Cure: "Acolyte",
  Blessing: "Acolyte",
  Revive: "Acolyte",
  HighHeal: "Acolyte",
  Sanctuary: "Priest",
  SacredGround: "Priest",
  TurnUndead: "Priest",
  Exorcism: "Priest",
  // Summoner / Necromancer
  SummonMastery: "Summoner",
  SummonWolf: "Summoner",
  SummonCat: "Summoner",
  SummonCactus: "Summoner",
  SummonAngel: "Summoner",
  SummonSkeleton: "Necromancer",
  SummonSkeletonMage: "Necromancer",
  SummonAbomination: "Necromancer",
  SummonWraith: "Necromancer",
  CorpseExplosion: "Necromancer",
  BoneSpear: "Necromancer",
  BoneSpikes: "Necromancer",
  Reanimation: "Necromancer",
  DeathCoil: "Necromancer",
};

function hintFor(skill) {
  if (curated[skill.id]) return curated[skill.id];
  const pref = skill.id.match(/^([A-Za-z]+)_/);
  if (pref && prefixToClass[pref[1]]) return prefixToClass[pref[1]];
  if (skill.spriteId) {
    const stem = skill.spriteId.match(/^([A-Za-z]+)/);
    if (stem && spriteStemToClass[stem[1]]) return spriteStemToClass[stem[1]];
  }
  return null;
}

const outSkills = skills.map((s) => {
  const className = hintFor(s);
  return {
    id: s.id,
    displayName: s.displayName,
    spriteId: s.spriteId,
    className,
    archetypeId: className != null ? ARCH[className] ?? null : null,
  };
});

const withClass = outSkills.filter((s) => s.className).length;
const out =
  "X:/projects/SpiritVale-Overlay/src/SpiritVale.Overlay.Domain/Resources/skill-catalog.json";
fs.writeFileSync(out, JSON.stringify({ skills: outSkills }));
console.log("skills", outSkills.length, "withClass", withClass);
