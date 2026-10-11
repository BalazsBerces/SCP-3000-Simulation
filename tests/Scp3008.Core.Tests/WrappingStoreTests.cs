using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class WrappingStoreTests
{
    private const float Tolerance = 1e-3f;

    private static StoreSpace NewStore()
    {
        var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        var space = sim.Snapshot.Space;
        sim.Dispose();
        return space;
    }

    private static void AssertClose(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < Tolerance, $"Expected {expected}, got {actual}.");

    [Fact]
    public void The_store_is_about_one_and_a_third_km_square_and_divided_into_chunks()
    {
        var store = NewStore();

        Assert.Equal(1344f, store.Size);
        Assert.Equal(64f, store.ChunkSize);
        Assert.Equal(21, store.ChunksPerSide);
        Assert.Equal(store.Size, store.ChunkSize * store.ChunksPerSide);
        Assert.Equal(441, store.ChunkCount);
    }

    [Theory]
    [InlineData(-10f, 1348f, 1334f, 4f)]
    [InlineData(1344f, 0f, 0f, 0f)]
    [InlineData(3 * 1344f + 5f, -2 * 1344f - 5f, 5f, 1339f)]
    [InlineData(700f, 300f, 700f, 300f)]
    public void Positions_off_the_floor_wrap_back_onto_it(float x, float y, float wrappedX, float wrappedY)
    {
        var store = NewStore();

        AssertClose(new Vector2(wrappedX, wrappedY), store.Wrap(new Vector2(x, y)));
    }

    [Fact]
    public void A_position_a_hair_before_the_origin_wraps_inside_the_floor()
    {
        var store = NewStore();

        var wrapped = store.Wrap(new Vector2(-1e-6f, -1e-6f));

        Assert.InRange(wrapped.X, 0f, store.Size - float.Epsilon);
        Assert.InRange(wrapped.Y, 0f, store.Size - float.Epsilon);
        Assert.Equal(new ChunkCoord(20, 20), store.ChunkOf(new Vector2(-1e-6f, -1e-6f)));
    }

    [Fact]
    public void Distance_and_direction_go_the_short_way_across_the_east_west_seam()
    {
        var store = NewStore();
        var nearEastEdge = new Vector2(1338f, 700f);
        var nearWestEdge = new Vector2(6f, 700f);

        Assert.Equal(12f, store.Distance(nearEastEdge, nearWestEdge), Tolerance);
        Assert.Equal(12f, store.Distance(nearWestEdge, nearEastEdge), Tolerance);
        AssertClose(new Vector2(12f, 0f), store.Delta(nearEastEdge, nearWestEdge));
        AssertClose(new Vector2(1f, 0f), store.Direction(nearEastEdge, nearWestEdge));
        AssertClose(new Vector2(-1f, 0f), store.Direction(nearWestEdge, nearEastEdge));
    }

    [Fact]
    public void Distance_and_direction_go_the_short_way_across_the_north_south_seam()
    {
        var store = NewStore();
        var nearNorthEdge = new Vector2(400f, 4f);
        var nearSouthEdge = new Vector2(400f, 1336f);

        Assert.Equal(12f, store.Distance(nearNorthEdge, nearSouthEdge), Tolerance);
        AssertClose(new Vector2(0f, -12f), store.Delta(nearNorthEdge, nearSouthEdge));
        AssertClose(new Vector2(0f, -1f), store.Direction(nearNorthEdge, nearSouthEdge));
        AssertClose(new Vector2(0f, 1f), store.Direction(nearSouthEdge, nearNorthEdge));
    }

    [Fact]
    public void Distance_and_direction_go_the_short_way_across_the_corner()
    {
        var store = NewStore();
        var nearSouthEastCorner = new Vector2(1338f, 1338f);
        var nearNorthWestCorner = new Vector2(3f, 6f);

        Assert.Equal(15f, store.Distance(nearSouthEastCorner, nearNorthWestCorner), Tolerance);
        AssertClose(new Vector2(9f, 12f), store.Delta(nearSouthEastCorner, nearNorthWestCorner));
        AssertClose(new Vector2(0.6f, 0.8f), store.Direction(nearSouthEastCorner, nearNorthWestCorner));
        AssertClose(new Vector2(-0.6f, -0.8f), store.Direction(nearNorthWestCorner, nearSouthEastCorner));
    }

    [Fact]
    public void Positions_a_whole_lap_apart_are_the_same_place()
    {
        var store = NewStore();
        var here = new Vector2(5f, 5f);
        var lapsAway = new Vector2(5f + 1344f, 5f - 2 * 1344f);

        Assert.Equal(0f, store.Distance(here, lapsAway), Tolerance);
        Assert.Equal(Vector2.Zero, store.Direction(here, here));
    }

    [Fact]
    public void Wrapped_distance_is_symmetric_and_never_longer_than_half_the_diagonal()
    {
        var store = NewStore();
        var random = new Random(1);
        Vector2 RandomPosition() => new((float)random.NextDouble() * 3 * store.Size - store.Size, (float)random.NextDouble() * 3 * store.Size - store.Size);

        for (var i = 0; i < 1000; i++)
        {
            var a = RandomPosition();
            var b = RandomPosition();

            var distance = store.Distance(a, b);

            Assert.InRange(distance, 0f, store.Size * MathF.Sqrt(2f) / 2f + Tolerance);
            Assert.Equal(distance, store.Distance(b, a), Tolerance);
            AssertClose(-store.Delta(a, b), store.Delta(b, a));
            AssertClose(store.Wrap(b), store.Wrap(a + store.Delta(a, b)));
        }
    }

    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(63.9f, 64f, 0, 1)]
    [InlineData(-0.5f, -0.5f, 20, 20)]
    [InlineData(1345f, 700f, 0, 10)]
    public void Every_position_falls_in_one_chunk(float x, float y, int chunkX, int chunkY)
    {
        var store = NewStore();

        Assert.Equal(new ChunkCoord(chunkX, chunkY), store.ChunkOf(new Vector2(x, y)));
    }

    [Fact]
    public void Chunks_around_the_corner_include_all_four_corners_of_the_store_placed_next_to_it()
    {
        var store = NewStore();

        var images = store.ChunkImagesAround(new Vector2(10f, 10f), reach: 100f).ToList();

        Assert.Equal(16, images.Count);
        Assert.Contains(new ChunkImage(new ChunkCoord(0, 0), Vector2.Zero), images);
        Assert.Contains(new ChunkImage(new ChunkCoord(20, 0), new Vector2(-1344f, 0f)), images);
        Assert.Contains(new ChunkImage(new ChunkCoord(0, 20), new Vector2(0f, -1344f)), images);
        Assert.Contains(new ChunkImage(new ChunkCoord(20, 20), new Vector2(-1344f, -1344f)), images);
        Assert.All(images, image =>
        {
            var placedOrigin = store.ChunkOrigin(image.Chunk) + image.Offset;
            Assert.InRange(placedOrigin.X, 10f - 100f - store.ChunkSize, 10f + 100f);
            Assert.InRange(placedOrigin.Y, 10f - 100f - store.ChunkSize, 10f + 100f);
        });
    }

    [Fact]
    public void A_reach_wider_than_the_store_shows_ghost_copies_of_the_same_chunk()
    {
        var store = NewStore();

        var images = store.ChunkImagesAround(new Vector2(672f, 672f), reach: 1000f)
            .Where(image => image.Chunk == new ChunkCoord(0, 0))
            .Select(image => image.Offset)
            .ToList();

        Assert.Equal(4, images.Count);
        Assert.Contains(new Vector2(0f, 0f), images);
        Assert.Contains(new Vector2(1344f, 0f), images);
        Assert.Contains(new Vector2(0f, 1344f), images);
        Assert.Contains(new Vector2(1344f, 1344f), images);
    }
}
