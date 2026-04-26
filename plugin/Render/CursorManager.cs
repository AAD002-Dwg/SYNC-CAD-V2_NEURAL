using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.ApplicationServices;

namespace HSync.Render
{
    /// <summary>
    /// Gestiona la visualización de los cursores de otros usuarios en tiempo real.
    /// Utiliza TransientManager para un renderizado de alto rendimiento sin tocar la BD.
    /// </summary>
    public static class CursorManager
    {
        private static readonly Dictionary<string, List<Entity>> _userCursors = new Dictionary<string, List<Entity>>();
        private static readonly IntegerCollection _viewportIds = new IntegerCollection();
        
        public static bool ShowCursors { get; set; } = true;
        public static string FollowUserId { get; set; } = null;

        public static void UpdateRemoteCursor(string userId, Point3d position)
        {
            if (userId == "ALAN-ACAD" || !ShowCursors) return; 

            var doc = Application.DocumentManager.MdiActiveDocument;
            var tm = TransientManager.CurrentTransientManager;

            // 1. Adaptive Scaling (Calculamos escala según el zoom actual)
            // VIEWSIZE es la altura de la ventana en unidades de dibujo
            double viewHeight = (double)Application.GetSystemVariable("VIEWSIZE");
            double scale = viewHeight * 0.015; // 1.5% de la pantalla para la cruz
            double textScale = viewHeight * 0.012; // 1.2% para el texto

            // 2. Follow Mode: Si estamos siguiendo a este usuario, centrar cámara
            if (userId == FollowUserId)
            {
                CenterView(doc.Editor, position);
            }

            // Limpiar anterior
            ClearUserCursor(userId);

            // 3. Crear representación visual adaptativa
            var cursorGroup = new List<Entity>();

            // Cruz (Crosshair)
            Line vLine = new Line(new Point3d(position.X, position.Y - scale, position.Z), new Point3d(position.X, position.Y + scale, position.Z));
            Line hLine = new Line(new Point3d(position.X - scale, position.Y, position.Z), new Point3d(position.X + scale, position.Y, position.Z));
            vLine.ColorIndex = 2;
            hLine.ColorIndex = 2;
            
            // Texto adaptativo
            var label = new DBText();
            label.TextString = userId;
            label.Position = new Point3d(position.X + (scale * 0.5), position.Y + (scale * 0.5), position.Z);
            label.Height = textScale; 
            label.ColorIndex = 2;

            cursorGroup.Add(vLine);
            cursorGroup.Add(hLine);
            cursorGroup.Add(label);

            foreach (var ent in cursorGroup)
            {
                tm.AddTransient(ent, TransientDrawingMode.DirectShortTerm, 128, _viewportIds);
            }

            _userCursors[userId] = cursorGroup;
        }

        private static void CenterView(Autodesk.AutoCAD.EditorInput.Editor ed, Point3d center)
        {
            using (var view = ed.GetCurrentView())
            {
                view.CenterPoint = new Point2d(center.X, center.Y);
                ed.SetCurrentView(view);
            }
        }

        public static void ClearUserCursor(string userId)
        {
            var tm = TransientManager.CurrentTransientManager;
            if (_userCursors.TryGetValue(userId, out List<Entity> entities))
            {
                foreach (var ent in entities)
                {
                    try { tm.EraseTransient(ent, _viewportIds); } catch { }
                    if (!ent.IsDisposed) ent.Dispose();
                }
                _userCursors.Remove(userId);
            }
        }

        public static void ClearAllCursors()
        {
            var tm = TransientManager.CurrentTransientManager;
            foreach (var group in _userCursors.Values)
            {
                foreach (var ent in group)
                {
                    try { tm.EraseTransient(ent, _viewportIds); } catch { }
                    if (!ent.IsDisposed) ent.Dispose();
                }
            }
            _userCursors.Clear();
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.UpdateScreen();
        }
    }
}
