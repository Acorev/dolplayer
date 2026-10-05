/*
 * FakePlayer - "faux client" réseau pour les faux joueurs.
 *
 * Implémente IPacketLib sans rien envoyer : chaque méthode ne fait rien et retourne la valeur par défaut.
 * Fichier à copier dans GameServerScripts avec FakePlayer.cs.
 * Généré à partir de GameServer/packets/Server/IPacketLib.cs (DOLSharp-master) : si votre IPacketLib
 * a d'autres méthodes, ajoutez-les ici sur le même modèle (une méthode vide).
 */

using System;
using System.Collections;
using System.Collections.Generic;

using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Geometry;
using DOL.GS.Housing;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.Profession;
using DOL.GS.Quests;

namespace DOL.GS.Scripts.FakePlayers
{
	public class NullPacketLib : IPacketLib
	{
		public int BowPrepare => default;
		public int BowShoot => default;
		public int OneDualWeaponHit => default;
		public int BothDualWeaponHit => default;
		public byte GetPacketCode(eServerPackets packetCode) { return default; }
		public void SendTCP(GSTCPPacketOut packet) { }
		public void SendTCP(byte[] buf) { }
		public void SendTCPRaw(GSTCPPacketOut packet) { }
		public void SendUDP(GSUDPPacketOut packet) { }
		public void SendUDP(byte[] buf) { }
		public void SendUDPRaw(GSUDPPacketOut packet) { }
		public void SendWarlockChamberEffect(GamePlayer player) { }
		public void SendVersionAndCryptKey() { }
		public void SendLoginDenied(eLoginError et) { }
		public void SendLoginGranted() { }
		public void SendLoginGranted(byte color) { }
		public void SendSessionID() { }
		public void SendPingReply(ulong timestamp, ushort sequence) { }
		public void SendRealm(eRealm realm) { }
		public void SendCharacterOverview(eRealm realm) { }
		public void SendDupNameCheckReply(string name, byte result) { }
		public void SendBadNameCheckReply(string name, bool bad) { }
		public void SendAttackMode(bool attackState) { }
		public void SendCharCreateReply(string name) { }
		public void SendCharStatsUpdate() { }
		public void SendCharResistsUpdate() { }
		public void SendRegions(ushort region) { }
		public void SendGameOpenReply() { }
		public void SendPlayerPositionAndObjectID() { }
		public void SendPlayerJump(bool headingOnly) { }
		public void SendPlayerInitFinished(byte mobs) { }
		public void SendUDPInitReply() { }
		public void SendTime() { }
		public void SendMessage(string msg, eChatType type, eChatLoc loc) { }
		public void SendPlayerCreate(GamePlayer playerToCreate) { }
		public void SendObjectGuildID(GameObject obj, Guild guild) { }
		public void SendPlayerQuit(bool totalOut) { }
		public void SendObjectRemove(GameObject obj) { }
		public void SendObjectCreate(GameObject obj) { }
		public void SendDebugMode(bool on) { }
		public void SendModelChange(GameObject obj, ushort newModel) { }
		public void SendModelAndSizeChange(GameObject obj, ushort newModel, byte newSize) { }
		public void SendModelAndSizeChange(ushort objectId, ushort newModel, byte newSize) { }
		public void SendEmoteAnimation(GameObject obj, eEmote emote) { }
		public void SendNPCCreate(GameNPC npc) { }
		public void SendLivingEquipmentUpdate(GameLiving living) { }
		public void SendRegionChanged() { }
		public void SendUpdatePoints() { }
		public void SendUpdateMoney() { }
		public void SendUpdateMaxSpeed() { }
		public void SendDelveInfo(string info) { }
		public void SendCombatAnimation(GameObject attacker, GameObject defender, ushort weaponID, ushort shieldID, int style, byte stance, byte result, byte targetHealthPercent) { }
		public void SendStatusUpdate() { }
		public void SendStatusUpdate(byte sittingFlag) { }
		public void SendSpellCastAnimation(GameLiving spellCaster, ushort spellID, ushort castingTime) { }
		public void SendSpellEffectAnimation(GameObject spellCaster, GameObject spellTarget, ushort spellid, ushort boltTime, bool noSound, byte success) { }
		public void SendRiding(GameObject rider, GameObject steed, bool dismount) { }
		public void SendFindGroupWindowUpdate(GamePlayer[] list) { }
		public virtual void SendGroupInviteCommand(GamePlayer invitingPlayer, string inviteMessage) { }
		public void SendDialogBox(eDialogCode code, ushort data1, ushort data2, ushort data3, ushort data4, eDialogType type, bool autoWrapText, string message) { }
		public void SendCustomDialog(string msg, CustomDialogResponse callback) { }
		public void SendCheckLOS(GameObject Checker, GameObject Target, CheckLOSResponse callback) { }
		public void SendCheckLOS(GameObject source, GameObject target, CheckLOSMgrResponse callback) { }
		public void SendGuildLeaveCommand(GamePlayer invitingPlayer, string inviteMessage) { }
		public void SendGuildInviteCommand(GamePlayer invitingPlayer, string inviteMessage) { }
		public void SendQuestOfferWindow(GameNPC questNPC, GamePlayer player, RewardQuest quest) { }
		public void SendQuestRewardWindow(GameNPC questNPC, GamePlayer player, RewardQuest quest) { }
		public void SendQuestOfferWindow(GameNPC questNPC, GamePlayer player, DataQuest quest) { }
		public void SendQuestRewardWindow(GameNPC questNPC, GamePlayer player, DataQuest quest) { }
		public void SendQuestOfferWindow(GameNPC questNPC, GamePlayer player, DQRewardQ quest) { }
		public void SendQuestRewardWindow(GameNPC questNPC, GamePlayer player, DQRewardQ quest) { }
		public void SendQuestSubscribeCommand(GameNPC invitingNPC, ushort questid, string inviteMessage) { }
		public void SendQuestAbortCommand(GameNPC abortingNPC, ushort questid, string abortMessage) { }
		public void SendGroupWindowUpdate() { }
		public void SendGroupMemberUpdate(bool updateIcons, bool updateMap, GameLiving living) { }
		public void SendGroupMembersUpdate(bool updateIcons, bool updateMap) { }
		public void SendInventoryItemsUpdate(ICollection<InventoryItem> itemsToUpdate) { }
		public void SendInventorySlotsUpdate(ICollection<int> slots) { }
		public void SendInventoryItemsUpdate(eInventoryWindowType windowType, ICollection<InventoryItem> itemsToUpdate) { }
		public void SendInventoryItemsUpdate(IDictionary<int, InventoryItem> updateItems, eInventoryWindowType windowType) { }
		public void SendDoorState(Region region, IDoor door) { }
		public void SendMerchantWindow(MerchantTradeItems itemlist, eMerchantWindowType windowType) { }
		public void SendMerchantWindow(MerchantCatalog catalog, eMerchantWindowType windowType) { }
		public void SendTradeWindow() { }
		public void SendCloseTradeWindow() { }
		public void SendPlayerDied(GamePlayer killedPlayer, GameObject killer) { }
		public void SendPlayerRevive(GamePlayer revivedPlayer) { }
		public void SendPlayerForgedPosition(GamePlayer player) { }
		public void SendUpdatePlayer() { }
		public void SendUpdatePlayerSkills() { }
		public void SendUpdateWeaponAndArmorStats() { }
		public void SendCustomTextWindow(string caption, IList<string> text) { }
		public void SendPlayerTitles() { }
		public void SendPlayerTitleUpdate(GamePlayer player) { }
		public void SendEncumberance() { }
		public void SendAddFriends(string[] friendNames) { }
		public void SendRemoveFriends(string[] friendNames) { }
		public void SendTimerWindow(string title, int seconds) { }
		public void SendCloseTimerWindow() { }
		public void SendCustomTrainerWindow(int type, List<Tuple<Specialization, List<Tuple<Skill, byte>>>> tree) { }
		public void SendChampionTrainerWindow(int type) { }
		public void SendTrainerWindow() { }
		public void SendInterruptAnimation(GameLiving living) { }
		public void SendDisableSkill(ICollection<Tuple<Skill, int>> skills) { }
		public void SendUpdateIcons(IList changedEffects, ref int lastUpdateEffectsCount) { }
		public void SendLevelUpSound() { }
		public void SendRegionEnterSound(byte soundId) { }
		public void SendDebugMessage(string format, params object[] parameters) { }
		public void SendDebugPopupMessage(string format, params object[] parameters) { }
		public void SendEmblemDialogue() { }
		public void SendWeather(uint x, uint width, ushort speed, ushort fogdiffusion, ushort intensity) { }
		public void SendPlayerModelTypeChange(GamePlayer player, byte modelType) { }
		public void SendObjectDelete(GameObject obj) { }
		public void SendObjectDelete(ushort oid) { }
		public void SendObjectUpdate(GameObject obj) { }
		public void SendQuestListUpdate() { }
		public void SendQuestUpdate(AbstractQuest quest) { }
		public void SendConcentrationList() { }
		public void SendUpdateCraftingSkills() { }
		public void SendChangeTarget(GameObject newTarget) { }

		[Obsolete]
		public void SendChangeGroundTarget(Point3D newTarget) { }
		public void SendChangeGroundTarget(Coordinate groundTarget) { }
		public void SendPetWindow(GameLiving pet, ePetWindowAction windowAction, eAggressionState aggroState, eWalkState walkState) { }
		public void SendPlaySound(eSoundType soundType, ushort soundID) { }
		public void SendNPCsQuestEffect(GameNPC npc, eQuestIndicator indicator) { }
		public void SendMasterLevelWindow(byte ml) { }
		public void SendHexEffect(GamePlayer player, byte effect1, byte effect2, byte effect3, byte effect4, byte effect5) { }
		public void SendRvRGuildBanner(GamePlayer player, bool show) { }
		public void SendSiegeWeaponAnimation(GameSiegeWeapon siegeWeapon) { }
		public void SendSiegeWeaponFireAnimation(GameSiegeWeapon siegeWeapon, int timer) { }
		public void SendSiegeWeaponCloseInterface() { }
		public void SendSiegeWeaponInterface(GameSiegeWeapon siegeWeapon, int time) { }
		public void SendLivingDataUpdate(GameLiving living, bool updateStrings) { }
		public void SendSoundEffect(ushort soundId, Position position, ushort radius) { }
		public void SendSoundEffect(ushort soundId, ushort zoneId, ushort x, ushort y, ushort z, ushort radius) { }
		public void SendKeepInfo(IGameKeep keep) { }
		public void SendKeepRealmUpdate(IGameKeep keep) { }
		public void SendKeepRemove(IGameKeep keep) { }
		public void SendKeepComponentInfo(IGameKeepComponent keepComponent) { }
		public void SendKeepComponentDetailUpdate(IGameKeepComponent keepComponent) { }
		public void SendKeepComponentRemove(IGameKeepComponent keepComponent) { }
		public void SendKeepClaim(IGameKeep keep, byte flag) { }
		public void SendKeepComponentUpdate(IGameKeep keep, bool LevelUp) { }
		public void SendKeepComponentInteract(IGameKeepComponent component) { }
		public void SendKeepComponentHookPoint(IGameKeepComponent component, int selectedHookPointIndex) { }
		public void SendClearKeepComponentHookPoint(IGameKeepComponent component, int selectedHookPointIndex) { }
		public void SendHookPointStore(GameKeepHookPoint hookPoint) { }
		public void SendWarmapUpdate(ICollection<IGameKeep> list) { }
		public void SendWarmapDetailUpdate(List<List<byte>> fights, List<List<byte>> groups) { }
		public void SendWarmapBonuses() { }
		public void SendHouse(House house) { }
		public void SendHouseOccupied(House house, bool flagHouseOccuped) { }
		public void SendRemoveHouse(House house) { }
		public void SendGarden(House house) { }
		public void SendGarden(House house, int i) { }
		public void SendEnterHouse(House house) { }
		public void SendExitHouse(House house, ushort unknown = 0) { }
		public void SendFurniture(House house) { }
		public void SendFurniture(House house, int i) { }
		public void SendHousePayRentDialog(string title) { }
		public void SendToggleHousePoints(House house) { }
		public void SendRentReminder(House house) { }
		public void SendMarketExplorerWindow(IList<InventoryItem> items, byte page, byte maxpage) { }
		public void SendMarketExplorerWindow() { }
		public void SendConsignmentMerchantMoney(long money) { }
		public void SendHouseUsersPermissions(House house) { }
		public void SendStarterHelp() { }
		public void SendPlayerFreeLevelUpdate() { }
		public void SendMovingObjectCreate(GameMovingObject obj) { }
		public void SendSetControlledHorse(GamePlayer player) { }
		public void SendControlledHorse(GamePlayer player, bool flag) { }
		public void CheckLengthHybridSkillsPacket(ref GSTCPPacketOut pak, ref int maxSkills, ref int first) { }
		public void SendNonHybridSpellLines() { }
		public void SendCrash(string str) { }
		public void SendRegionColorScheme() { }
		public void SendRegionColorScheme(byte color) { }
		public void SendVampireEffect(GameLiving living, bool show) { }
		public void SendXFireInfo(byte flag) { }
		public void SendMinotaurRelicMapRemove(byte id) { }
		public void SendMinotaurRelicMapUpdate(byte id, ushort region, int x, int y, int z) { }
		public void SendMinotaurRelicMapUpdate(byte id, Position position) { }
		public void SendMinotaurRelicWindow(GamePlayer player, int spell, bool flag) { }
		public void SendMinotaurRelicBarUpdate(GamePlayer player, int xp) { }
		public void SendBlinkPanel(byte flag) { }
	}
}
