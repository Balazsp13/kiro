// LayerService.cs
//
// Ensures the layers declared in symbols.config.json (name, ACI color, linetype)
// exist in the drawing before any geometry is drawn. Also provides a generic
// EnsureLayer used by renderers that need an ad-hoc layer (raster, legend, etc.).

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Helyszinrajz.Config;

namespace Helyszinrajz
{
    /// <summary>Creates and configures layers used by the renderers.</summary>
    public sealed class LayerService
    {
        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly Editor? _ed;

        public LayerService(Database db, Transaction tr, Editor? ed = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _tr = tr ?? throw new ArgumentNullException(nameof(tr));
            _ed = ed;
        }

        /// <summary>
        /// Ensure every layer defined in the symbol catalog exists with its
        /// configured ACI color and linetype.
        /// </summary>
        public void EnsureCatalogLayers(SymbolCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            foreach (SymbolDef def in catalog.Definitions)
            {
                EnsureLayer(def.Layer, def.Color, def.Linetype);
            }
        }

        /// <summary>
        /// Ensure a single layer exists. If it already exists its properties are
        /// left untouched (the house template wins). ACI color 0 or negative is
        /// ignored. Missing linetypes fall back to CONTINUOUS.
        /// </summary>
        public ObjectId EnsureLayer(string? name, short aciColor = 7, string? linetype = null)
        {
            string layerName = string.IsNullOrWhiteSpace(name) ? "0" : name!.Trim();

            LayerTable lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
            {
                return lt[layerName];
            }

            lt.UpgradeOpen();
            var layer = new LayerTableRecord { Name = layerName };

            if (aciColor > 0)
            {
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, aciColor);
            }

            string lt1 = string.IsNullOrWhiteSpace(linetype) ? "CONTINUOUS" : linetype!.Trim();
            layer.LinetypeObjectId = EnsureLinetype(lt1);

            ObjectId id = lt.Add(layer);
            _tr.AddNewlyCreatedDBObject(layer, true);
            return id;
        }

        /// <summary>Lock or unlock a layer (used to lock the raster layer).</summary>
        public void SetLocked(string name, bool locked)
        {
            LayerTable lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
            {
                return;
            }

            var ltr = (LayerTableRecord)_tr.GetObject(lt[name], OpenMode.ForWrite);
            ltr.IsLocked = locked;
        }

        /// <summary>Ensure a linetype is loaded; load from acad.lin when needed.</summary>
        private ObjectId EnsureLinetype(string linetype)
        {
            var ltt = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetype))
            {
                return ltt[linetype];
            }

            // Try to load the linetype definition from the standard linetype file.
            try
            {
                _db.LoadLineTypeFile(linetype, "acad.lin");
                ltt = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(linetype))
                {
                    return ltt[linetype];
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                _ed?.WriteMessage($"\nFigyelmeztetes: a(z) '{linetype}' vonaltipus nem toltheto be ({ex.Message}); CONTINUOUS lesz hasznalva.");
            }

            // Fall back to the always-present Continuous linetype.
            return _db.ContinuousLinetype;
        }
    }
}
