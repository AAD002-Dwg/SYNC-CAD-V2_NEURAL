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

        public static void UpdateRemoteCursor(string userId, Point3d position)
        {
            // Evitar procesar nuestro propio cursor (aunque el Hub ya lo filtra)
            if (userId == "ALAN-ACAD") return; 

            var tm = TransientManager.CurrentTransientManager;

            // Limpiar cursor anterior del usuario
            if (_userCursors.TryGetValue(userId, out List<Entity> entities))
            {
                foreach (var ent in entities)
                {
                    tm.EraseTransient(ent, _viewportIds);
                    ent.Dispose();
                }
                _userCursors.Remove(userId);
            }

            // Crear nueva representación visual del cursor
            var cursorGroup = new List<Entity>();

            // 1. El "Punto" del cursor (un pequeño círculo)
            var dot = new Circle() { Center = position, Radius = 15.0, ColorIndex = 2 }; // Amarillo
            
            // 2. Etiqueta con el nombre del usuario
            var label = new DBText();
            label.TextString = userId;
            label.Position = new Point3d(position.X + 20, position.Y + 20, position.Z);
            label.Height = 25.0;
            label.ColorIndex = 2;

            cursorGroup.Add(dot);
            cursorGroup.Add(label);

            foreach (var ent in cursorGroup)
            {
                tm.AddTransient(ent, TransientDrawingMode.DirectShortTerm, 128, _viewportIds);
            }

            _userCursors[userId] = cursorGroup;
        }

        public static void ClearAllCursors()
        {
            var tm = TransientManager.CurrentTransientManager;
            foreach (var group in _userCursors.Values)
            {
                foreach (var ent in group)
                {
                    tm.EraseTransient(ent, _viewportIds);
                    ent.Dispose();
                }
            }
            _userCursors.Clear();
        }
    }
}
