// RasterRenderer.cs
//
// Attaches the e-kozmu base-map PDF/image as a raster/underlay and positions it
// in model space. This is the ONLY renderer that applies the georeference
// transform: the two reference points map the raster's image/paper coordinate
// space onto model meters, supplying the base map's position, rotation, AND
// uniform scale. (For the Lajosmizse sample that scale is 0.5: image delta 200
// maps to model delta 100.) All other geometry is authored in model meters and
// is placed directly, so applying the same scale here is what makes the base
// map and the drawn plan share one coordinate system.
//
// The attachment lives on a dedicated, locked layer ("ALATERKEP-RASZTER") so
// the base map cannot be edited by accident. PDFs are attached as a
// PdfReference (DWF/PDF underlay); other image formats are attached as a
// RasterImage. Missing files are logged and skipped.

using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Model;

namespace Helyszinrajz.Rendering
{
    /// <summary>Attaches and scales the base-map raster/underlay.</summary>
    public sealed class RasterRenderer
    {
        /// <summary>Dedicated locked layer that holds the base map.</summary>
        public const string RasterLayer = "ALATERKEP-RASZTER";

        private readonly RenderContext _ctx;

        public RasterRenderer(RenderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void Render(Raster raster)
        {
            if (raster == null || string.IsNullOrWhiteSpace(raster.File))
            {
                _ctx.Log("Raszter: nincs megadva alaterkep fajl, kihagyva.");
                return;
            }

            string path = ResolvePath(raster.File);
            if (!File.Exists(path))
            {
                _ctx.Log($"Raszter: az alaterkep fajl nem talalhato: '{path}'. Beillesztes kihagyva.");
                return;
            }

            // Dedicated locked layer for the base map.
            _ctx.Layers.EnsureLayer(RasterLayer, 8, "CONTINUOUS");

            // Insertion point, rotation, and scale all come from the georeference
            // transform (image/paper space -> model meters).
            Point3d insertion = _ctx.Geo.ToModelPoint3d(raster.Attach.InsertionPoint);
            double rotationRad = (raster.Attach.RotationDeg * Math.PI / 180.0) + _ctx.Geo.RotationRad;
            double geoScale = _ctx.Geo.Scale;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".pdf")
                {
                    AttachPdf(path, insertion, rotationRad, geoScale);
                }
                else
                {
                    AttachRasterImage(path, insertion, rotationRad, geoScale);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                _ctx.Log($"Raszter: a beillesztes sikertelen ({ex.Message}). Kihagyva.");
                return;
            }

            // Lock the raster layer so the base map is read-only.
            _ctx.Layers.SetLocked(RasterLayer, true);
            _ctx.Log($"Raszter beillesztve 1:{raster.Attach.ScaleDenominator} meretaranyban: {Path.GetFileName(path)}");
        }

        /// <summary>
        /// Attach a PDF page as a PDF underlay. A PdfDefinition is posted to the
        /// drawing's underlay-definition dictionary, then a PdfReference points at it.
        /// </summary>
        private void AttachPdf(string path, Point3d insertion, double rotationRad, double geoScale)
        {
            // The georeference transform's uniform scale maps the PDF underlay's
            // image/paper space onto model meters, so the base map lines up with
            // the model-meter geometry drawn by the other renderers.
            var def = new PdfDefinition();
            def.SetSourceFileName(path);
            def.SetActivePage("1");

            // Register the definition in the PDF underlay-definition dictionary.
            string defKey = Path.GetFileNameWithoutExtension(path);
            ObjectId defId = GetOrAddUnderlayDefinition(def, defKey);

            var underlay = new PdfReference
            {
                DefinitionId = defId,
                Position = insertion,
                Rotation = rotationRad,
                ScaleFactors = new Scale3d(geoScale, geoScale, geoScale),
                Layer = RasterLayer,
            };
            underlay.SetDatabaseDefaults();
            underlay.Layer = RasterLayer;

            _ctx.Append(underlay);
        }

        /// <summary>
        /// Post the PdfDefinition to the PDF underlay-definition dictionary
        /// (creating the dictionary if needed) and return its ObjectId.
        /// </summary>
        private ObjectId GetOrAddUnderlayDefinition(PdfDefinition def, string key)
        {
            string dictKey = PdfDefinition.GetDictionaryKey();
            var nod = (DBDictionary)_ctx.Tr.GetObject(_ctx.Db.NamedObjectsDictionaryId, OpenMode.ForRead);

            DBDictionary underlayDict;
            if (nod.Contains(dictKey))
            {
                underlayDict = (DBDictionary)_ctx.Tr.GetObject(nod.GetAt(dictKey), OpenMode.ForWrite);
            }
            else
            {
                nod.UpgradeOpen();
                underlayDict = new DBDictionary();
                nod.SetAt(dictKey, underlayDict);
                _ctx.Tr.AddNewlyCreatedDBObject(underlayDict, true);
            }

            if (underlayDict.Contains(key))
            {
                return underlayDict.GetAt(key);
            }

            ObjectId id = underlayDict.SetAt(key, def);
            _ctx.Tr.AddNewlyCreatedDBObject(def, true);
            return id;
        }

        /// <summary>Attach a bitmap image (png/jpg/tif) as a RasterImage.</summary>
        private void AttachRasterImage(string path, Point3d insertion, double rotationRad, double geoScale)
        {
            ObjectId defId = RasterImageDef.GetImageDictionary(_ctx.Db);
            if (defId.IsNull)
            {
                defId = RasterImageDef.CreateImageDictionary(_ctx.Db);
            }

            var imageDict = (DBDictionary)_ctx.Tr.GetObject(defId, OpenMode.ForWrite);
            string defName = Path.GetFileNameWithoutExtension(path);

            var imageDef = new RasterImageDef { SourceFileName = path };
            imageDef.Load();
            ObjectId imageDefId = imageDict.SetAt(defName, imageDef);
            _ctx.Tr.AddNewlyCreatedDBObject(imageDef, true);

            var image = new RasterImage
            {
                ImageDefId = imageDefId,
                Layer = RasterLayer,
            };

            // The RasterImage orientation's u/v vectors carry both rotation and
            // scale: their length is the model size of one image unit. Scale them
            // by the georeference's uniform scale so the base map maps onto model
            // meters consistently with the drawn geometry.
            Vector3d uVec = Vector3d.XAxis.RotateBy(rotationRad, Vector3d.ZAxis) * geoScale;
            Vector3d vVec = Vector3d.YAxis.RotateBy(rotationRad, Vector3d.ZAxis) * geoScale;
            image.Orientation = new CoordinateSystem3d(insertion, uVec, vVec);
            image.ShowImage = true;

            _ctx.Append(image);
            RasterImage.EnableReactors(true);
            image.AssociateRasterDef(imageDef);
        }

        /// <summary>Resolve a raster file path relative to the spec directory when not absolute.</summary>
        private string ResolvePath(string file)
        {
            if (Path.IsPathRooted(file))
            {
                return file;
            }

            return Path.GetFullPath(Path.Combine(_ctx.SpecDirectory, file));
        }
    }
}
