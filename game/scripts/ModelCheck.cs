using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Checks the external Furniture models (<see cref="FurnitureModels"/>): every variant of the hero
/// types has one; each fitted full mesh fills its Furniture size (footprint and height) within
/// <see cref="Tolerance"/> and keeps to its triangle budget; the coarse mesh stays inside that size,
/// passes <see cref="SilhouetteCheck"/> and has no more triangles; every material has albedo, normal and roughness maps; the licence is CC0 or CC-BY;
/// and CREDITS.md lists the model with its source, author and licence. Run by DevCapture's
/// <c>--model-check</c>.
/// </summary>
public static class ModelCheck
{
    /// <summary>The Furniture types drawn from external models, every variant.</summary>
    public static readonly FurnitureType[] HeroTypes =
        [FurnitureType.Armchair, FurnitureType.Sofa, FurnitureType.Chair, FurnitureType.Toilet, FurnitureType.VanityUnit,
         FurnitureType.DiningTable, FurnitureType.Sideboard, FurnitureType.MealPlate, FurnitureType.Rug,
         FurnitureType.ShoppingCart, FurnitureType.ShelfUnit, FurnitureType.ExitSign, FurnitureType.DoorFrame];

    // In metres.
    private const float Tolerance = 0.001f;

    /// <summary>One line per check that failed or passed; true if every check passed.</summary>
    public static (bool Passed, List<string> Lines) Run()
    {
        var lines = new List<string>();
        var failed = 0;
        void Check(bool ok, string line)
        {
            lines.Add($"{(ok ? "ok  " : "FAIL")} {line}");
            if (!ok)
                failed++;
        }
        var credits = Godot.FileAccess.GetFileAsString("res://CREDITS.md").Split('\n');
        foreach (var type in HeroTypes)
            for (var variant = 0; variant < FurnitureCatalog.VariantCount(type); variant++)
                Check(FurnitureModels.Find(type, variant) is not null, $"{type} {variant} is drawn from a model");
        foreach (var model in FurnitureModels.All)
        {
            var name = $"{model.Type} {model.Variant} ({model.Folder})";
            var spec = FurnitureCatalog.Spec(model.Type, model.Variant);
            var size = new Vector3(spec.Width, spec.Height, spec.Depth);
            var expected = new Aabb(new Vector3(-spec.Width / 2f, 0f, -spec.Depth / 2f), size);
            var full = FurnitureModels.Full(model);
            var coarse = FurnitureModels.Coarse(model);
            var bounds = full.GetAabb();
            var (low, high) = ((bounds.Position - expected.Position).Abs(), (bounds.End - expected.End).Abs());
            var error = new[] { low.X, low.Y, low.Z, high.X, high.Y, high.Z }.Max();
            Check(error <= Tolerance, FormattableString.Invariant(
                $"{name} fills {spec.Width:0.###} x {spec.Depth:0.###} x {spec.Height:0.###} m (W x D x H): bounds {bounds.Size.X:0.####} x {bounds.Size.Z:0.####} x {bounds.Size.Y:0.####} m from {bounds.Position.X:0.####}, {bounds.Position.Y:0.####}, {bounds.Position.Z:0.####}"));
            var coarseBounds = coarse.GetAabb();
            Check(expected.Grow(Tolerance).Encloses(coarseBounds), FormattableString.Invariant(
                $"{name} coarse stays inside its Furniture size: bounds {coarseBounds.Size.X:0.####} x {coarseBounds.Size.Z:0.####} x {coarseBounds.Size.Y:0.####} m"));
            var silhouette = SilhouetteCheck.Compare(model.Type, model.Variant);
            Check(silhouette.Passed, $"{name} coarse silhouette: {silhouette}");
            var (fullTriangles, coarseTriangles) = (FurnitureModels.Triangles(full), FurnitureModels.Triangles(coarse));
            Check(fullTriangles > 0 && fullTriangles <= model.TriangleBudget, $"{name} has {fullTriangles} triangles, budget {model.TriangleBudget}");
            Check(coarseTriangles > 0 && coarseTriangles <= fullTriangles, $"{name} coarse has {coarseTriangles} triangles, at most the full {fullTriangles}");
            Check(coarse.GetSurfaceCount() == 1, $"{name} coarse has {coarse.GetSurfaceCount()} surface(s), one wanted");
            for (var s = 0; s < full.GetSurfaceCount(); s++)
            {
                var material = (ShaderMaterial)full.SurfaceGetMaterial(s);
                bool Has(string texture) => material.GetShaderParameter(texture).Obj is Texture2D;
                Check((bool)material.GetShaderParameter("textured") && (bool)material.GetShaderParameter("normal_enabled")
                      && Has("albedo_texture") && Has("normal_texture") && Has("roughness_texture"),
                    $"{name} surface {full.SurfaceGetName(s)} has albedo, normal and roughness maps");
            }
            Check(FurnitureModels.AllowedLicences.Contains(model.Licence), $"{name} licence {model.Licence} is CC0 or CC-BY");
            Check(credits.Any(l => l.Contains(model.Source) && l.Contains(model.Author) && l.Contains(model.Licence) && l.Contains($"models/{model.Folder}")),
                $"{name} is in CREDITS.md with source {model.Source}, author {model.Author} and licence {model.Licence}");
            if (model.Type == FurnitureType.ExitSign)
            {
                Check(Enumerable.Range(0, full.GetSurfaceCount()).Any(s =>
                {
                    var material = (ShaderMaterial)full.SurfaceGetMaterial(s);
                    return (float)material.GetShaderParameter("emission_energy") > 0f
                        && !(bool)material.GetShaderParameter("lamp_glow")
                        && (bool)material.GetShaderParameter("emission_textured");
                }), $"{name} preserves the sign's emissive texture independently of Lamps");
                var coarseMaterial = (ShaderMaterial)coarse.SurfaceGetMaterial(0);
                Check(coarseMaterial.GetShaderParameter("part_glow").AsVector3Array().Any(v => v.LengthSquared() > 0f),
                    $"{name} still glows in the coarse mesh");
            }
        }
        lines.Add($"{FurnitureModels.All.Count} models, {failed} checks failed.");
        return (failed == 0, lines);
    }
}
