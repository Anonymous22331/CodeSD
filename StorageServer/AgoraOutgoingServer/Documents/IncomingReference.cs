using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.BackOffice.ObjectModel;

namespace Agora.Outgoing.Server
{
    public sealed partial class ValidationExtension
    {
        /// <summary>
        /// Формирует реквизиты связанного входящего документа.
        /// </summary>
        private string IncomingReferenceLine(UserSession session, ObjectContext context, Document document)
        {
            if (document.MainInfo.ReferenceList == null)
            {
                return "";
            }

            var config = new XmlDocument();
            config.XmlResolver = null;
            config.Load(@"C:\AGORA-OUT\server\incoming-mapping.xml");
            var mapping = config.DocumentElement;
            Guid incomingKind = new Guid(mapping.GetAttribute("kind"));
            foreach (var reference in document.MainInfo.ReferenceList.References.OrderBy(r => r.CreationDate).ThenBy(r => r.GetObjectId()))
            {
                if (reference.Card == Guid.Empty || (reference.CardType != Guid.Empty && reference.CardType != DocumentType))
                {
                    continue;
                }

                var linked = session.CardManager.GetCardData(reference.Card);
                if (linked.Type.Id != DocumentType)
                {
                    continue;
                }

                var sys = linked.Sections[SystemSection].FirstRow;
                Guid kindId = Id(sys, "Kind");
                if (kindId == Guid.Empty)
                {
                    continue;
                }

                var kind = context.GetObject<KindsCardKind>(kindId);
                var seen = new HashSet<Guid>();
                bool incoming = false;
                while (kind != null && seen.Add(kind.GetObjectId()))
                {
                    if (kind.GetObjectId() == incomingKind)
                    {
                        incoming = true;
                        break;
                    }

                    kind = kind.ParentCardKind;
                }

                if (!incoming)
                {
                    continue;
                }

                // The oldest incoming link is authoritative; do not silently substitute a later one.
                var numberRow = linked.Sections[new Guid(mapping.GetAttribute("numberSection"))].FirstRow;
                string number = numberRow == null ? null : numberRow.GetString(mapping.GetAttribute("numberField"));
                if (String.IsNullOrWhiteSpace(number))
                {
                    return "";
                }

                string line = "На №" + number.Trim();
                string dateField = mapping.GetAttribute("dateField");
                if (!String.IsNullOrEmpty(dateField))
                {
                    var dateRow = linked.Sections[new Guid(mapping.GetAttribute("dateSection"))].FirstRow;
                    DateTime? date = dateRow == null ? null : dateRow.GetDateTime(dateField);
                    if (date.HasValue)
                    {
                        line += " от " + date.Value.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    Trace("Incoming sender date mapping pending for Docsvision 5.5; no registration date substituted.");
                }

                return line;
            }

            return "";
        }
    }
}
