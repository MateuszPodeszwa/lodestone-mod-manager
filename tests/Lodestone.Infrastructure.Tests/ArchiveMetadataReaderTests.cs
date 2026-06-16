using Lodestone.Application.Abstractions;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.Archives;

namespace Lodestone.Infrastructure.Tests;

public class ArchiveMetadataReaderTests
{
    private readonly ArchiveMetadataReader _reader = new();

    [Fact]
    public async Task Reads_fabric_mod_with_dependencies()
    {
        using var dir = new TempDir();
        string jar = ZipFixtures.Create(dir.File("sodium.jar"),
            ("fabric.mod.json", """
            {
              "id": "sodium",
              "name": "Sodium",
              "version": "0.5.8",
              "depends": { "fabricloader": ">=0.15", "minecraft": "1.21.4", "fabric-api": "*" },
              "breaks": { "optifabric": "*" }
            }
            """));

        Result<LocalContentMetadata> result = await _reader.ReadAsync(jar);

        result.IsSuccess.ShouldBeTrue();
        LocalContentMetadata meta = result.Value;
        meta.Type.ShouldBe(ContentType.Mod);
        meta.ModId.ShouldBe("sodium");
        meta.Name.ShouldBe("Sodium");
        meta.LoadersOrEmpty.ShouldContain(Loader.Fabric);
        // The loader/game pseudo-deps are skipped; the real one is kept.
        meta.DependenciesOrEmpty.ShouldContain(d => d.Identifier == "fabric-api" && d.Kind == DependencyKind.Required);
        meta.DependenciesOrEmpty.ShouldContain(d => d.Identifier == "optifabric" && d.Kind == DependencyKind.Incompatible);
        meta.DependenciesOrEmpty.ShouldNotContain(d => d.Identifier == "minecraft");
    }

    [Fact]
    public async Task Reads_forge_mods_toml()
    {
        using var dir = new TempDir();
        string jar = ZipFixtures.Create(dir.File("jei.jar"),
            ("META-INF/mods.toml", """
            modLoader="javafml"
            loaderVersion="[47,)"
            [[mods]]
            modId="jei"
            version="19.5.0"
            displayName="Just Enough Items"
            [[dependencies.jei]]
            modId="forge"
            mandatory=true
            [[dependencies.jei]]
            modId="patchouli"
            mandatory=false
            """));

        LocalContentMetadata meta = (await _reader.ReadAsync(jar)).Value;

        meta.Type.ShouldBe(ContentType.Mod);
        meta.ModId.ShouldBe("jei");
        meta.Name.ShouldBe("Just Enough Items");
        meta.Version.ShouldBe("19.5.0");
        meta.LoadersOrEmpty.ShouldContain(Loader.Forge);
        meta.DependenciesOrEmpty.ShouldContain(d => d.Identifier == "patchouli" && d.Kind == DependencyKind.Optional);
        meta.DependenciesOrEmpty.ShouldNotContain(d => d.Identifier == "forge"); // loader pseudo-dep skipped
    }

    [Fact]
    public async Task Reads_quilt_mod()
    {
        using var dir = new TempDir();
        string jar = ZipFixtures.Create(dir.File("mod.jar"),
            ("quilt.mod.json", """
            {
              "quilt_loader": {
                "id": "examplemod",
                "version": "1.0.0",
                "metadata": { "name": "Example Mod" },
                "depends": [ { "id": "quilted_fabric_api" } ]
              }
            }
            """));

        LocalContentMetadata meta = (await _reader.ReadAsync(jar)).Value;

        meta.ModId.ShouldBe("examplemod");
        meta.Name.ShouldBe("Example Mod");
        meta.LoadersOrEmpty.ShouldContain(Loader.Quilt);
        meta.DependenciesOrEmpty.ShouldContain(d => d.Identifier == "quilted_fabric_api");
    }

    [Fact]
    public async Task Detects_resource_pack_by_pack_mcmeta()
    {
        using var dir = new TempDir();
        string zip = ZipFixtures.Create(dir.File("faithful.zip"),
            ("pack.mcmeta", """{ "pack": { "pack_format": 34, "description": "Faithful" } }"""));

        (await _reader.ReadAsync(zip)).Value.Type.ShouldBe(ContentType.ResourcePack);
    }

    [Fact]
    public async Task Detects_shader_pack_by_shaders_folder()
    {
        using var dir = new TempDir();
        string zip = ZipFixtures.Create(dir.File("bsl.zip"),
            ("shaders/world0/gbuffers_basic.fsh", "// shader code"));

        (await _reader.ReadAsync(zip)).Value.Type.ShouldBe(ContentType.Shader);
    }

    [Fact]
    public async Task Fails_on_a_non_archive_file()
    {
        using var dir = new TempDir();
        string bad = dir.File("not-a-zip.jar");
        await System.IO.File.WriteAllTextAsync(bad, "this is not a zip");

        Result<LocalContentMetadata> result = await _reader.ReadAsync(bad);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("archive.invalid");
    }
}
