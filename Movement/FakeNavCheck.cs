/*
 * FakePlayers - Movement/FakeNavCheck.cs
 *
 * Outil de diagnostic pour la commande /fake nav : vérifie si le navmesh chargé par le serveur
 * correspond au terrain du jeu, à l'endroit où se trouve le joueur.
 *
 * Pourquoi : le déplacement des alts pourra s'appuyer sur le navmesh (chemins qui contournent
 * les obstacles). Mais un navmesh généré pour un autre terrain (ex. client modifié 1.68) aurait
 * une hauteur (Z) décalée : les alts flotteraient ou s'enfonceraient. Cet outil mesure l'écart.
 *
 * Ce qu'il mesure :
 *   1. le point du navmesh le plus proche de vos pieds : écart en hauteur et à l'horizontale ;
 *   2. si vous avez une cible dans la même zone : le navmesh trouve-t-il un chemin jusqu'à elle ?
 */

using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.Geometry;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// Diagnostic du navmesh à la position d'un joueur.
	/// </summary>
	public static class FakeNavCheck
	{
		/// <summary>Écart (en unités du jeu) en dessous duquel le navmesh est considéré juste.</summary>
		public const int OK_DISTANCE = 32;

		/// <summary>Écart au-delà duquel le navmesh est considéré décalé.</summary>
		public const int BAD_DISTANCE = 100;

		/// <summary>Rayon de recherche du point de navmesh le plus proche.</summary>
		private const float SEARCH_RANGE = 256f;

		/// <summary>
		/// Fait le diagnostic et renvoie les lignes à afficher au joueur.
		/// </summary>
		public static List<string> Check(GamePlayer player)
		{
			var lines = new List<string>();
			Zone zone = player.CurrentZone;
			if (zone == null)
			{
				lines.Add("Zone inconnue.");
				return lines;
			}

			string zoneName = "zone " + zone.ID + " (" + zone.Description + ")";

			// Le serveur peut tourner sans navmesh du tout.
			if (!PathingMgr.Instance.IsAvailable)
			{
				lines.Add("Le pathing est désactivé sur le serveur.");
				return lines;
			}
			if (!PathingMgr.Instance.HasNavmesh(zone))
			{
				lines.Add("Pas de navmesh pour la " + zoneName + ".");
				return lines;
			}

			// --- 1. Le point du navmesh le plus proche de vos pieds.
			var feet = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);
			Vector3? nav = PathingMgr.Instance.GetClosestPointAsync(zone, feet, SEARCH_RANGE, SEARCH_RANGE, SEARCH_RANGE);
			if (nav == null)
			{
				lines.Add("Navmesh " + zoneName + " : AUCUNE surface à moins de " + (int)SEARCH_RANGE
					+ " unités de vous (navmesh très décalé, ou vous êtes hors des zones marchables).");
			}
			else
			{
				int dz = (int)Math.Round(nav.Value.Z - feet.Z);
				int dxy = (int)Math.Round(Vector2.Distance(new Vector2(nav.Value.X, nav.Value.Y), new Vector2(feet.X, feet.Y)));
				int worst = Math.Max(Math.Abs(dz), dxy);

				string verdict = worst <= OK_DISTANCE ? "OK"
					: worst <= BAD_DISTANCE ? "léger écart"
					: "DÉCALÉ";

				lines.Add("Navmesh " + zoneName + " : " + verdict
					+ " (hauteur " + (dz >= 0 ? "+" : "") + dz + ", horizontal " + dxy + ").");
			}

			// --- 2. Chemin jusqu'à la cible, si elle est dans la même zone.
			GameObject target = player.TargetObject;
			if (target != null && target != player)
			{
				if (target.CurrentZone != zone)
				{
					lines.Add("Cible " + target.Name + " : dans une autre zone, chemin non testé.");
				}
				else
				{
					var (path, error) = PathingMgr.Instance.GetPathStraightAsync(zone, player.Position.Coordinate, target.Position.Coordinate);
					if (error != PathingError.PathFound || path.PointCount == 0)
					{
						lines.Add("Chemin vers " + target.Name + " : AUCUN chemin trouvé.");
					}
					else
					{
						int miss = (int)Math.Round(path.End.DistanceTo(target.Position.Coordinate));
						lines.Add("Chemin vers " + target.Name + " : trouvé, " + path.PointCount + " points"
							+ (miss > OK_DISTANCE ? ", mais s'arrête à " + miss + " unités de la cible." : "."));
					}
				}
			}

			return lines;
		}
	}
}
