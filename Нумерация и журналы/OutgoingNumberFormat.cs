using System;
using System.Globalization;
namespace Agora.Outgoing {
 public static class OutgoingNumberFormat {
  public static string Format(string journal,bool provisional,int number) {
   if(journal!="ГО" && journal!="16" && journal!="64")throw new ArgumentException("Неизвестный журнал регистрации.","journal");
   if(number<1)throw new ArgumentOutOfRangeException("number","Порядковый номер должен быть положительным.");
   return (provisional?"Исх/Вр -":"Исх -")+number.ToString("D3",CultureInfo.InvariantCulture)+"/"+journal;
  }
 }
}