/*
 * FakePlayers - Movement/FakeFollowAction.cs
 *
 * Fait suivre son propriétaire à un alt. Un timer par alt, toutes les TICK_MS millisecondes.
 *
 * À chaque tic : pendant une incantation l'alt ne bouge pas ; puis la RÉSURRECTION du propriétaire
 * (Combat/FakeRez), les SOINS (Combat/FakeHeals), puis les CHANTS (Combat/FakeChants), puis les BUFFS hors combat (Combat/FakeBuffs), puis le COMBAT (Combat/FakeCombat) :
 * l'alt ne suit son propriétaire que s'il n'a rien à soigner, buffer ni combattre. Puis, dans l'ordre :
 *   1. le propriétaire a changé de région → l'alt est supprimé puis rappelé à côté de lui
 *      (un faux client ne peut pas faire le changement de région d'un vrai joueur) ;
 *   2. l'alt est en mode "stay" (/fake stay) ou mort → il ne bouge pas ;
 *   3. le propriétaire est très loin (téléportation, alt bloqué) → l'alt réapparaît sur lui ;
 *   4. l'alt est à sa place → il s'arrête et se tourne vers le propriétaire ;
 *      file indienne : le 1er alt s'arrête à FIRST_DISTANCE, le suivant STEP_DISTANCE plus loin, etc. ;
 *   5. sinon il marche vers le propriétaire en suivant le chemin du navmesh
 *      (recalculé quand le propriétaire a bougé ; ligne droite si pas de navmesh dans la zone).
 *
 * Hauteur (Z) : pour un joueur, le jeu affiche exactement la hauteur envoyée par le serveur
 * (il ne "pose" pas le personnage au sol comme il le fait pour un mob). Or :
 *   - la surface du navmesh est un peu au-dessus du sol (mesuré avec /fake nav : environ +8) ;
 *   - le navmesh ne donne un point qu'aux changements de direction : entre deux points éloignés,
 *     la ligne droite passe au-dessus des creux du terrain.
 * Donc le chemin est "resserré" (un point tous les DENSIFY_STEP), chaque point est recalé sur la
 * hauteur du navmesh à cet endroit moins NAVMESH_Z_OFFSET, et l'alt est recalé à l'arrêt.
 *
 * Pendant qu'il marche, sa position est envoyée aux joueurs proches à chaque tic (animation fluide).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.GS.Geometry;
using log4net;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Le suivi du propriétaire par un alt.
	/// </summary>
	public static class FakeFollowAction
	{
		/// <summary>Journal du serveur (console + fichier de log) pour écrire les erreurs.</summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>Intervalle entre deux tics, en millisecondes.</summary>
		public const int TICK_MS = 250;

		/// <summary>Distance d'arrêt du 1er alt derrière le propriétaire.</summary>
		public const int FIRST_DISTANCE = 150;

		/// <summary>Distance supplémentaire pour chaque alt suivant (file indienne).</summary>
		public const int STEP_DISTANCE = 100;

		/// <summary>Au-delà de cette distance, l'alt réapparaît directement sur le propriétaire.</summary>
		public const int TELEPORT_DISTANCE = 2500;

		/// <summary>Le chemin est recalculé quand le propriétaire s'est éloigné de plus de cette distance.</summary>
		public const int REPLAN_DISTANCE = 100;

		/// <summary>Un point du chemin est considéré atteint en dessous de cette distance.</summary>
		private const int WAYPOINT_REACHED = 16;

		/// <summary>Au-delà de la distance d'arrêt + cette marge, l'alt accélère pour rattraper.</summary>
		private const int CATCH_UP_MARGIN = 400;

		/// <summary>Bonus de vitesse pour rattraper (1.3 = +30 %).</summary>
		private const double CATCH_UP_FACTOR = 1.3;

		/// <summary>
		/// Hauteur de la surface du navmesh au-dessus du vrai sol, retirée à toutes les hauteurs
		/// venant du navmesh. À ajuster si les alts flottent encore (augmenter) ou s'enfoncent (diminuer).
		/// </summary>
		public const int NAVMESH_Z_OFFSET = 8;

		/// <summary>Écart maximal entre deux points du chemin, pour suivre le relief.</summary>
		public const int DENSIFY_STEP = 64;

		/// <summary>Zone de recherche du navmesh autour d'un point pour trouver la hauteur du sol.</summary>
		private const float GROUND_SEARCH_XY = 32f;
		private const float GROUND_SEARCH_Z = 128f;

		/// <summary>Vitesse de course par défaut d'un joueur, si le serveur n'en donne pas.</summary>
		internal const short DEFAULT_SPEED = 191;

		/// <summary>Le chemin en cours d'un alt (un par alt, gardé par son timer). Partagé avec Combat/FakeCombat.</summary>
		internal class FollowState
		{
			public List<Coordinate> Path = new();       // points du chemin, sans le point de départ
			public int Index;                           // prochain point à atteindre
			public Coordinate PlannedFor = Coordinate.Nowhere; // destination au calcul du chemin
			public bool ErrorLogged;
		}

		/// <summary>
		/// Démarre le suivi. À appeler APRÈS AddToWorld (le timer utilise la région de l'alt).
		/// Le timer s'arrête tout seul quand l'alt est supprimé.
		/// </summary>
		public static void Start(FakeGamePlayer fake)
		{
			var state = new FollowState();
			new RegionTimer(fake, t => Tick(fake, state)).Start(TICK_MS);
		}

		/// <summary>Un tic du timer. Renvoie le délai avant le prochain tic (0 = arrêt du timer).</summary>
		private static int Tick(FakeGamePlayer fake, FollowState state)
		{
			// Alt supprimé : on arrête.
			if (fake.ObjectState == GameObject.eObjectState.Deleted || fake.Client.ClientState != GameClient.eClientState.Playing)
				return 0;

			try
			{
				GamePlayer owner = fake.Owner;

				// Propriétaire absent ou en plein chargement (changement de région) : on attend.
				if (owner == null || owner.ObjectState != GameObject.eObjectState.Active)
				{
					Stop(fake, state);
					return TICK_MS;
				}

				// --- 1. Changement de région : supprimé puis rappelé à côté du propriétaire.
				if (owner.CurrentRegionID != fake.CurrentRegionID)
				{
					// Le rappel se fait en arrière-plan (voir FakePlayerMgr.Recall) ;
					// ce timer s'arrête, le nouvel alt aura le sien.
					FakePlayerMgr.Recall(fake);
					return 0;
				}

				if (Update(fake, owner, state))
					FakePositionSender.SendNow(fake);
			}
			catch (Exception ex)
			{
				// On logue une seule fois pour ne pas inonder la console.
				if (!state.ErrorLogged)
				{
					log.Warn("[FakePlayers] erreur de déplacement pour " + fake.Name, ex);
					state.ErrorLogged = true;
				}
			}
			return TICK_MS;
		}

		/// <summary>
		/// Décide du mouvement de l'alt pour ce tic.
		/// </summary>
		/// <returns>true si la position a changé (il faut l'envoyer aux joueurs proches).</returns>
		private static bool Update(FakeGamePlayer fake, GamePlayer owner, FollowState state)
		{
			// --- Aggro : corrections en attente selon le pourcentage de sa classe (voir Combat/FakeAggro).
			FakeAggro.Flush(fake);

			// --- Mort : on ne bouge plus et on ne combat plus.
			if (!fake.IsAlive)
			{
				FakeCombat.EndFight(fake);
				return Stop(fake, state);
			}

			// --- Incantation en cours : on ne bouge pas (bouger l'interromprait) et on ne change pas de cible.
			if (fake.IsCasting)
				return Stop(fake, state);

			// --- Résurrection du propriétaire, combat fini (voir Combat/FakeRez).
			if (FakeRez.Update(fake, owner, state, out bool rezMoved))
				return rezMoved;

			// --- Soins d'abord (voir Combat/FakeHeals) : soigner passe avant tout le reste.
			if (FakeHeals.Update(fake, owner, state, out bool healMoved))
				return healMoved;

			// --- Chants ensuite (voir Combat/FakeChants) : garder les bons chants actifs.
			if (FakeChants.Update(fake, owner, state, out bool chantMoved))
				return chantMoved;

			// --- Buffs ensuite, hors combat seulement (voir Combat/FakeBuffs).
			if (FakeBuffs.Update(fake, owner, state, out bool buffMoved))
				return buffMoved;

			// --- Combat ensuite (voir Combat/FakeCombat) : l'alt ne suit que s'il n'a rien à combattre.
			GameLiving target = FakeCombat.ChooseTarget(fake, owner);
			if (target != null)
				return FakeCombat.Fight(fake, target, state);
			FakeCombat.EndFight(fake);

			// --- 2. Mode "stay" : on ne bouge pas.
			if (!fake.IsFollowing)
				return Stop(fake, state);

			Coordinate here = fake.Position.Coordinate;
			Coordinate there = owner.Position.Coordinate;
			double distance = here.DistanceTo(there);

			// --- 3. Très loin : réapparaît sur le propriétaire.
			if (distance > TELEPORT_DISTANCE)
			{
				fake.StopWalking();
				fake.Position = owner.Position;
				state.Path.Clear();
				UpdateZone(fake);
				return true;
			}

			// --- 4. À sa place dans la file : arrêt, tourné vers le propriétaire.
			int stopAt = StopDistance(fake, owner);
			if (distance <= stopAt)
			{
				if (!fake.IsWalking)
					return false;
				Stop(fake, state);
				// Recalé au sol là où il s'arrête, et tourné vers le propriétaire.
				Coordinate ground = SnapToGround(fake.CurrentZone, fake.Position.Coordinate);
				fake.Position = fake.Position.With(coordinate: ground).With(orientation: ground.GetOrientationTo(there));
				return true;
			}

			// --- 5. Marche vers le propriétaire.
			return Approach(fake, state, there, Speed(fake, owner, distance, stopAt));
		}

		/// <summary>
		/// Fait marcher l'alt vers un point en suivant le chemin du navmesh (recalculé si le point a bougé).
		/// Utilisé pour suivre le propriétaire, et par Combat/FakeCombat pour aller au contact d'une cible.
		/// C'est l'appelant qui décide quand s'arrêter.
		/// </summary>
		/// <returns>true (la position change : il faut l'envoyer aux joueurs proches).</returns>
		internal static bool Approach(FakeGamePlayer fake, FollowState state, Coordinate there, short speed)
		{
			Coordinate here = fake.Position.Coordinate;

			if (state.Index >= state.Path.Count || state.PlannedFor.DistanceTo(there) > REPLAN_DISTANCE)
				Plan(fake, there, state);

			// Points déjà atteints : on passe au suivant.
			while (state.Index < state.Path.Count && here.DistanceTo(state.Path[state.Index]) <= WAYPOINT_REACHED)
				state.Index++;

			Coordinate next = state.Index < state.Path.Count ? state.Path[state.Index] : there;

			if (fake.WalkDestination != next || fake.CurrentSpeed != speed)
				fake.WalkTowards(next, speed);

			UpdateZone(fake);
			return true;
		}

		/// <summary>
		/// Calcule le chemin vers le propriétaire avec le navmesh.
		/// Sans navmesh dans la zone (ou sans chemin trouvé) : ligne droite vers le propriétaire.
		/// </summary>
		private static void Plan(FakeGamePlayer fake, Coordinate destination, FollowState state)
		{
			state.Path.Clear();
			state.Index = 0;
			state.PlannedFor = destination;

			Zone zone = fake.CurrentZone;
			if (zone != null && PathingMgr.Instance.HasNavmesh(zone))
			{
				var (path, error) = PathingMgr.Instance.GetPathStraightAsync(zone, fake.Position.Coordinate, destination);
				if (error == PathingError.PathFound && path.PointCount > 0)
				{
					// LinePath ne donne ses points qu'un par un : on les recopie dans une liste.
					var points = new List<Coordinate>();
					while (path.CurrentWayPoint != Coordinate.Nowhere)
					{
						points.Add(path.CurrentWayPoint);
						path.SelectNextWayPoint();
					}
					points.Add(path.End);

					// Points resserrés et recalés au sol, pour suivre le relief.
					state.Path.AddRange(Densify(zone, points));
					return;
				}
			}

			// Secours : ligne droite.
			state.Path.Add(destination);
		}

		/// <summary>
		/// Ajoute des points intermédiaires (un tous les DENSIFY_STEP au plus) entre les points du chemin,
		/// et recale chaque point sur la hauteur du sol.
		/// </summary>
		private static List<Coordinate> Densify(Zone zone, List<Coordinate> points)
		{
			var result = new List<Coordinate>();
			for (int i = 0; i < points.Count; i++)
			{
				Coordinate b = points[i];
				if (i > 0)
				{
					Coordinate a = points[i - 1];
					double length = a.DistanceTo(b, ignoreZ: true);
					int steps = (int)(length / DENSIFY_STEP);
					for (int s = 1; s <= steps; s++)
					{
						double t = (double)s / (steps + 1);
						var middle = Coordinate.Create(
							(int)Math.Round(a.X + (b.X - a.X) * t),
							(int)Math.Round(a.Y + (b.Y - a.Y) * t),
							(int)Math.Round(a.Z + (b.Z - a.Z) * t));
						result.Add(SnapToGround(zone, middle));
					}
				}
				result.Add(SnapToGround(zone, b));
			}

			// Le 1er point est la position de départ de l'alt : inutile d'y "marcher".
			if (result.Count > 1)
				result.RemoveAt(0);
			return result;
		}

		/// <summary>
		/// Hauteur du sol à cet endroit : surface du navmesh la plus proche, moins NAVMESH_Z_OFFSET.
		/// Sans navmesh (ou sans surface trouvée), le point est rendu tel quel.
		/// </summary>
		private static Coordinate SnapToGround(Zone zone, Coordinate point)
		{
			if (zone == null || !PathingMgr.Instance.HasNavmesh(zone))
				return point;

			Vector3? nav = PathingMgr.Instance.GetClosestPointAsync(zone,
				new Vector3(point.X, point.Y, point.Z), GROUND_SEARCH_XY, GROUND_SEARCH_XY, GROUND_SEARCH_Z);
			if (nav == null)
				return point;

			return point.With(z: (int)Math.Round(nav.Value.Z) - NAVMESH_Z_OFFSET);
		}

		/// <summary>Arrête l'alt et oublie son chemin. Renvoie true s'il marchait (position à envoyer).</summary>
		internal static bool Stop(FakeGamePlayer fake, FollowState state)
		{
			state.Path.Clear();
			state.Index = 0;
			if (!fake.IsWalking)
				return false;
			fake.StopWalking();
			return true;
		}

		/// <summary>
		/// Distance d'arrêt de cet alt : sa place dans la file indienne
		/// (ordre d'appel parmi les alts du même propriétaire).
		/// </summary>
		private static int StopDistance(FakeGamePlayer fake, GamePlayer owner)
		{
			int rank = FakePlayerMgr.GetFakesOf(owner).IndexOf(fake);
			return FIRST_DISTANCE + Math.Max(0, rank) * STEP_DISTANCE;
		}

		/// <summary>
		/// Vitesse de marche : la plus grande entre la vitesse de course de l'alt et celle du propriétaire,
		/// avec un bonus s'il est très en retard.
		/// </summary>
		private static short Speed(FakeGamePlayer fake, GamePlayer owner, double distance, int stopAt)
		{
			double speed = fake.MaxSpeed > 0 ? fake.MaxSpeed : DEFAULT_SPEED;
			speed = Math.Max(speed, owner.CurrentSpeed);
			if (distance > stopAt + CATCH_UP_MARGIN)
				speed *= CATCH_UP_FACTOR;
			return (short)Math.Min(speed, short.MaxValue);
		}

		/// <summary>
		/// Tient à jour la zone courante de l'alt, comme le gestionnaire de position d'un vrai joueur
		/// (sans les messages "Vous entrez dans...").
		/// </summary>
		private static void UpdateZone(FakeGamePlayer fake)
		{
			Zone zone = fake.CurrentZone;
			if (zone != null && zone != fake.LastPositionUpdateZone)
				fake.LastPositionUpdateZone = zone;
		}
	}
}
