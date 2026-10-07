/*
 * FakePlayers - Combat/FakeTrade.cs
 *
 * L'ÉCHANGE entre le propriétaire et un de ses alts, dans un seul sens : du propriétaire vers l'alt
 * (par exemple lui donner une épée, puis se connecter sur ce personnage pour l'équiper).
 *
 * Fonctionnement :
 *   - le propriétaire pose un objet ou de l'argent sur l'alt, comme avec un vrai joueur :
 *     la fenêtre d'échange s'ouvre (côté alt, elle existe mais personne ne la voit) ;
 *   - l'alt n'échange qu'avec son propriétaire : un autre joueur reçoit un refus ;
 *   - l'alt ne propose jamais rien ;
 *   - l'alt accepte APRÈS le propriétaire : quand celui-ci clique sur "Accepter", l'alt accepte au tic
 *     suivant (au plus 250 ms) et l'échange se fait. Tout changement dans la fenêtre annule les deux
 *     acceptations (règle du jeu) : le propriétaire garde la main jusqu'au bout ;
 *   - pendant l'échange, l'alt reste sur place (sinon la distance pourrait fermer la fenêtre).
 * L'alt étant sauvegardé (Core/FakeGamePlayer), ce qu'il reçoit est enregistré.
 */

using System.Reflection;
using DOL.GS.PacketHandler;
using log4net;

namespace DOL.GS.Scripts.FakePlayers
{
	/// <summary>
	/// L'échange propriétaire → alt.
	/// </summary>
	public static class FakeTrade
	{
		/// <summary>Journal du serveur (console + fichier de log).</summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// "A-t-il accepté ?" : DOLSharp ne le rend pas public dans la fenêtre d'échange,
		/// on le lit donc directement (champ m_tradeAccept de PlayerTradeWindow).
		/// </summary>
		private static readonly FieldInfo ACCEPTED_FIELD =
			typeof(PlayerTradeWindow).GetField("m_tradeAccept", BindingFlags.NonPublic | BindingFlags.Instance);

		private static bool _fieldErrorLogged;

		/// <summary>
		/// true si ce joueur peut échanger avec l'alt (seulement son propriétaire).
		/// Sinon, le joueur reçoit un message de refus.
		/// </summary>
		public static bool IsAllowedPartner(FakeGamePlayer fake, GamePlayer source)
		{
			if (source != null && source == fake.Owner)
				return true;
			source?.Out.SendMessage(fake.Name + " n'échange qu'avec son propriétaire.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			return false;
		}

		/// <summary>
		/// Gère l'échange en cours, s'il y en a un.
		/// </summary>
		/// <returns>true si un échange est ouvert (l'alt reste alors sur place).</returns>
		internal static bool Update(FakeGamePlayer fake, GamePlayer owner)
		{
			ITradeWindow mine = fake.TradeWindow;
			if (mine == null)
				return false;

			// Échange qui n'est pas avec le propriétaire (ne devrait pas arriver) : on le ferme.
			if (mine.Partner != owner || owner.TradeWindow == null)
			{
				mine.CloseTrade();
				return false;
			}

			// L'alt accepte seulement après son propriétaire.
			if (HasAccepted(owner.TradeWindow) && !HasAccepted(mine))
				mine.AcceptTrade();

			return true;
		}

		/// <summary>true si le propriétaire de cette fenêtre a cliqué sur "Accepter" (depuis le dernier changement).</summary>
		private static bool HasAccepted(ITradeWindow window)
		{
			if (ACCEPTED_FIELD == null || window is not PlayerTradeWindow)
			{
				if (!_fieldErrorLogged)
				{
					_fieldErrorLogged = true;
					log.Warn("[FakePlayers] échange : impossible de lire l'acceptation (PlayerTradeWindow.m_tradeAccept introuvable).");
				}
				return false;
			}
			return (bool)ACCEPTED_FIELD.GetValue(window);
		}
	}
}
