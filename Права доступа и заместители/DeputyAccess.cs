using System;
using System.Linq;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  static bool AuthorizedDeputy(ObjectContext c,Guid principalId,Guid actorId,bool signing) {
   if(principalId==Guid.Empty || actorId==Guid.Empty)return false;
   if(principalId==actorId)return true;
   var principal=c.GetObject<StaffEmployee>(principalId);var actor=c.GetObject<StaffEmployee>(actorId);
   if(actor.Status!=StaffEmployeeStatus.Active || actor.NotAvailable)return false;
   var candidates=principal.Deputies.Where(d=>d.Employee!=null && d.Employee.GetObjectId()==actorId && (signing?d.Signature:d.DeputyAccess)).ToArray();
   if(candidates.Any(d=>d.Permanent))return true;
   return principal.Status!=StaffEmployeeStatus.Active && principal.ActiveEmployee!=null && principal.ActiveEmployee.GetObjectId()==actorId && candidates.Length>0;
  }
  static bool AuthorOrDeputy(UserSession session,Guid author,Guid actor){return AuthorizedDeputy(DocumentContext(session),author,actor,false);}
  static bool RegistrarOrDeputy(ObjectContext c,Guid actor,Guid groupId) {
   var staff=c.GetService<IStaffService>();var employee=c.GetObject<StaffEmployee>(actor);var group=c.GetObject<StaffGroup>(groupId);
   if(staff.IsEmployeeInGroup(employee,group,true))return true;
   return staff.FindDeputiesWhereEmployeeIsDeputy(employee).Any(d=>d.ParentEmployee!=null && AuthorizedDeputy(c,d.ParentEmployee.GetObjectId(),actor,false) && staff.IsEmployeeInGroup(d.ParentEmployee,group,true));
  }
 }
}
