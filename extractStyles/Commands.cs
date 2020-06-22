using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace extractStyles
{
  public class Commands
  {
    [CommandMethod("extractStyles")]
    public static void ExtracStyles()
    {
      // this should contain all Tin Srufaces with respective array of points
      JArray styleInformation = new JArray();

      Document doc = Application.DocumentManager.MdiActiveDocument;
      Database db = doc.Database;
      using (Transaction trans = doc.TransactionManager.StartTransaction())
      {
        BlockTable bt = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
        BlockTableRecord mSpace = trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

        foreach (ObjectId entId in mSpace)
        {
          // try open as Civil 3D entity, otherwise skip
          Autodesk.Aec.DatabaseServices.Entity aecEnt = trans.GetObject(entId, OpenMode.ForRead) as Autodesk.Aec.DatabaseServices.Entity;
          if (aecEnt == null) continue;

          // Handle is exposed as "externalId" in the Viewer
          dynamic entityInfo = new JObject();
          entityInfo.handle = aecEnt.Handle.ToString();
          entityInfo.style = new JArray();

          StyleBase style;
          try { style = trans.GetObject(aecEnt.StyleId, OpenMode.ForRead) as StyleBase; }
          catch { continue; }

          dynamic nameProp = new JObject();
          nameProp.name = "Name";
          nameProp.value = style.Name;
          entityInfo.style.Add(nameProp);

          Type styleType = style.GetType();
          foreach (PropertyInfo prop in styleType.GetProperties())
          {
            try
            {
              // if the property is not a Civil 3D property, skip...
              if (!prop.DeclaringType.FullName.Contains("Civil")) continue;

              string propName = prop.Name;
              object rawValue = prop.GetValue(style);
              string formattedValue = string.Empty;

              if (rawValue is String)
                formattedValue = rawValue as string;
              else if (rawValue is ObjectId)
              {
                StyleBase subStyle = trans.GetObject((ObjectId)rawValue, OpenMode.ForRead) as StyleBase;
                if (subStyle == null) return;

                formattedValue = subStyle.Name;
              }

              if (string.IsNullOrWhiteSpace(formattedValue)) continue;

              dynamic styleProp = new JObject();
              styleProp.name = Regex.Replace(propName, "(\\B[A-Z])", " $1");
              styleProp.value = formattedValue;
              entityInfo.style.Add(styleProp);
            }
            catch { }
          }

          styleInformation.Add(entityInfo);
        }

        trans.Commit();
      }

      // save all to a .json file
      using (StreamWriter file = File.CreateText("result.json"))
      using (JsonTextWriter writer = new JsonTextWriter(file))
      {
        styleInformation.WriteTo(writer);
      }
    }
  }
}
