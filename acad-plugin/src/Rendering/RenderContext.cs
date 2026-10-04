// RenderContext.cs
//
// Shared state passed to every renderer: the active database/transaction, the
// model-space BlockTableRecord, the Editor for logging, the georeference
// transform, and the loaded configs. Centralizing these avoids threading a long
// parameter list through each renderer.
//
// IMPORTANT coordinate convention: all spec geometry (symbol positions, cable
// vertices, label positions, legend/title-block/BOM placements) is already in
// model meters, so renderers place it directly with ToPoint3d(DtoPoint). The
// georeference transform (Geo) exists ONLY to map the raster base map's
// image/paper space onto model meters, and is used solely by RasterRenderer.
// Do NOT run model-meter geometry through Geo; doing so double-counts the
// image->model scale and shrinks the plan relative to the base map.

using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Config;
using Helyszinrajz.Model;

namespace Helyszinrajz.Rendering
{
    /// <summary>Carries the common objects every renderer needs.</summary>
    public sealed class RenderContext
    {
        public Database Db { get; }
        public Transaction Tr { get; }
        public Editor Ed { get; }
        public BlockTableRecord ModelSpace { get; }
        public GeoReferenceTransform Geo { get; }
        public SymbolCatalog Symbols { get; }
        public TitleBlockLayout TitleBlock { get; }
        public LayerService Layers { get; }

        /// <summary>Directory of the drawing-spec JSON; used to resolve the raster file path.</summary>
        public string SpecDirectory { get; }

        public RenderContext(
            Database db,
            Transaction tr,
            Editor ed,
            BlockTableRecord modelSpace,
            GeoReferenceTransform geo,
            SymbolCatalog symbols,
            TitleBlockLayout titleBlock,
            LayerService layers,
            string specDirectory)
        {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            Tr = tr ?? throw new ArgumentNullException(nameof(tr));
            Ed = ed ?? throw new ArgumentNullException(nameof(ed));
            ModelSpace = modelSpace ?? throw new ArgumentNullException(nameof(modelSpace));
            Geo = geo ?? throw new ArgumentNullException(nameof(geo));
            Symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
            TitleBlock = titleBlock ?? throw new ArgumentNullException(nameof(titleBlock));
            Layers = layers ?? throw new ArgumentNullException(nameof(layers));
            SpecDirectory = specDirectory ?? string.Empty;
        }

        /// <summary>Append an entity to model space inside the current transaction.</summary>
        public ObjectId Append(Entity ent)
        {
            ObjectId id = ModelSpace.AppendEntity(ent);
            Tr.AddNewlyCreatedDBObject(ent, true);
            return id;
        }

        public void Log(string message) => Ed.WriteMessage("\n" + message);

        /// <summary>
        /// Convert a spec DTO point (already in model meters) to an AutoCAD
        /// Point3d at z = 0. Use this for all model-meter geometry; do NOT pass
        /// such points through the georeference transform (that is for the raster
        /// base map only).
        /// </summary>
        public static Point3d ToPoint3d(DtoPoint p) => new(p.X, p.Y, 0.0);
    }
}
