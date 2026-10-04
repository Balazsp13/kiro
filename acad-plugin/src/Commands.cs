// Commands.cs
//
// AutoCAD command entry point. HELYSZINRAJZ_IMPORT prompts for (or accepts) a
// drawing-spec JSON path, loads the symbol/title-block configs (from a config
// directory next to the JSON, or the plugin default), opens ONE transaction,
// and runs every renderer in dependency order:
//
//     layers -> raster -> symbols -> cables -> labels -> legend -> titleblock -> bom
//
// then commits and reports a summary to the Editor. All work is wrapped in
// try/catch with Editor error reporting; the schema_version in meta is checked
// against the version this plugin targets and a mismatch is warned (not fatal).

using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Helyszinrajz.Config;
using Helyszinrajz.Model;
using Helyszinrajz.Rendering;

[assembly: CommandClass(typeof(Helyszinrajz.Commands))]

namespace Helyszinrajz
{
    /// <summary>Command methods exposed to AutoCAD.</summary>
    public sealed class Commands
    {
        /// <summary>Drawing-spec schema version this plugin was built against.</summary>
        public const string SupportedSchemaVersion = "1.0.0";

        [CommandMethod("HELYSZINRAJZ_IMPORT")]
        public void HelyszinrajzImport()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                string? specPath = PromptForSpecPath(ed);
                if (string.IsNullOrWhiteSpace(specPath))
                {
                    ed.WriteMessage("\nHELYSZINRAJZ_IMPORT: megszakitva, nincs rajz-spec fajl.");
                    return;
                }

                if (!File.Exists(specPath))
                {
                    ed.WriteMessage($"\nHiba: a rajz-spec fajl nem talalhato: '{specPath}'.");
                    return;
                }

                DrawingSpec spec = SpecLoader.Load(specPath);
                WarnOnSchemaMismatch(ed, spec);

                string specDir = Path.GetDirectoryName(Path.GetFullPath(specPath)) ?? Directory.GetCurrentDirectory();
                (SymbolCatalog symbols, TitleBlockLayout titleBlock) = LoadConfigs(ed, specDir);

                GeoReferenceTransform geo = GeoReferenceTransform.FromGeoreference(spec.Georeference);

                RenderAll(doc, db, ed, spec, geo, symbols, titleBlock, specDir);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nHELYSZINRAJZ_IMPORT HIBA: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }

        private static void RenderAll(
            Document doc,
            Database db,
            Editor ed,
            DrawingSpec spec,
            GeoReferenceTransform geo,
            SymbolCatalog symbols,
            TitleBlockLayout titleBlock,
            string specDir)
        {
            using DocumentLock docLock = doc.LockDocument();
            using Transaction tr = db.TransactionManager.StartTransaction();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var layerService = new LayerService(db, tr, ed);
            var ctx = new RenderContext(db, tr, ed, modelSpace, geo, symbols, titleBlock, layerService, specDir);

            // 1. Layers: ensure config layers + any plain layer names from the spec.
            layerService.EnsureCatalogLayers(symbols);
            foreach (string layerName in spec.Layers)
            {
                layerService.EnsureLayer(layerName);
            }

            // 2. Raster base map.
            new RasterRenderer(ctx).Render(spec.Raster);

            // 3. Symbols.
            new SymbolRenderer(ctx).Render(spec.Symbols);

            // 4. Cable runs.
            new CableRenderer(ctx).Render(spec.CableRuns);

            // 5. Free-text labels.
            new LabelRenderer(ctx).Render(spec.Labels);

            // 6. Legend (jelmagyarazat): placed to the right of the plan extents.
            Point3d legendOrigin = new(140.0, 60.0, 0.0);
            new LegendRenderer(ctx).Render(spec.Legend, legendOrigin);

            // 7. Title block (rajzpecset): house block or fallback frame.
            Point3d titleOrigin = new(140.0, 0.0, 0.0);
            new TitleBlockRenderer(ctx).Render(spec, titleOrigin);

            // 8. BOM (anyagjegyzek) table.
            Point3d bomOrigin = new(140.0, 100.0, 0.0);
            new BomRenderer(ctx).Render(spec.Bom, bomOrigin);

            tr.Commit();

            ReportSummary(ed, spec);
        }

        private static string? PromptForSpecPath(Editor ed)
        {
            var pfo = new PromptOpenFileOptions("\nValassza ki a rajz-spec JSON fajlt")
            {
                Filter = "Drawing spec JSON (*.json)|*.json|Minden fajl (*.*)|*.*",
            };

            PromptFileNameResult res = ed.GetFileNameForOpen(pfo);
            if (res.Status != PromptStatus.OK)
            {
                return null;
            }

            return res.StringResult;
        }

        /// <summary>
        /// Load symbols.config.json and titleblock.config.json. Search order:
        /// a "config" directory next to the spec, then the spec directory itself,
        /// then a "config" directory next to this plugin assembly.
        /// </summary>
        private static (SymbolCatalog, TitleBlockLayout) LoadConfigs(Editor ed, string specDir)
        {
            string symbolsPath = FindConfig("symbols.config.json", specDir);
            string titleBlockPath = FindConfig("titleblock.config.json", specDir);

            ed.WriteMessage($"\nKonfiguracio: {symbolsPath}");
            ed.WriteMessage($"\nKonfiguracio: {titleBlockPath}");

            SymbolCatalog symbols = SymbolCatalog.Load(symbolsPath);
            TitleBlockLayout titleBlock = TitleBlockLayout.Load(titleBlockPath);
            return (symbols, titleBlock);
        }

        private static string FindConfig(string fileName, string specDir)
        {
            string[] candidates =
            {
                Path.Combine(specDir, "config", fileName),
                Path.Combine(specDir, fileName),
                Path.Combine(PluginDir(), "config", fileName),
                Path.Combine(PluginDir(), fileName),
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Return the primary expected location so the loader throws a clear error.
            return candidates[0];
        }

        private static string PluginDir()
        {
            string? dir = Path.GetDirectoryName(typeof(Commands).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
        }

        private static void WarnOnSchemaMismatch(Editor ed, DrawingSpec spec)
        {
            string version = spec.Meta?.SchemaVersion ?? "";
            if (!string.Equals(version, SupportedSchemaVersion, StringComparison.Ordinal))
            {
                ed.WriteMessage(
                    $"\nFIGYELEM: a rajz-spec sema verzioja '{version}', a plugin a(z) '{SupportedSchemaVersion}' verziora keszult. " +
                    "A feldolgozas folytatodik, de ellenorizze az eredmenyt.");
            }
        }

        private static void ReportSummary(Editor ed, DrawingSpec spec)
        {
            ed.WriteMessage("\n--- HELYSZINRAJZ_IMPORT kesz ---");
            ed.WriteMessage($"\nProjekt: {spec.Project.Subject}");
            ed.WriteMessage($"\nRajzszam: {spec.Project.Rajzszam}   Meretarany: 1:{spec.Project.ScaleDenominator}");
            ed.WriteMessage($"\nJelek: {spec.Symbols.Count}, Kabel nyomvonalak: {spec.CableRuns.Count}, " +
                            $"Feliratok: {spec.Labels.Count}, Jelmagyarazat: {spec.Legend.Count}, BOM sorok: {spec.Bom.Count}");
            ed.WriteMessage($"\nKabel teljes hossza: {spec.Summary.KabelTeljesHosszaM:0.##} m");
            ed.WriteMessage("\nMEGJEGYZES: emberi tervezoi ellenorzes szukseges a kiadas elott.");
        }
    }
}
