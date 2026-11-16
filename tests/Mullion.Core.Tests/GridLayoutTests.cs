using Mullion.Core;

namespace Mullion.Core.Tests;

public class GridLayoutTests
{
    private static readonly RectI Area = new(0, 0, 1920, 1040);

    [Fact]
    public void Edges_cover_the_whole_area()
    {
        var layout = new GridLayout(new RectI(100, 50, 1466, 818), 7, 5);

        Assert.Equal(100, layout.ColumnEdge(0));
        Assert.Equal(1466, layout.ColumnEdge(7));
        Assert.Equal(50, layout.RowEdge(0));
        Assert.Equal(818, layout.RowEdge(5));
    }

    [Fact]
    public void Whole_grid_selection_fills_the_area()
    {
        var layout = new GridLayout(Area, 4, 4);

        Assert.Equal(Area, layout.WindowBounds(new CellSpan(0, 0, 3, 3)));
    }

    [Fact]
    public void Single_cell_bounds_are_a_quarter_in_each_direction()
    {
        var layout = new GridLayout(Area, 4, 4);

        Assert.Equal(new RectI(480, 260, 960, 520), layout.WindowBounds(new CellSpan(1, 1, 1, 1)));
    }

    [Theory]
    [InlineData(1366, 768, 7, 5)]
    [InlineData(1920, 1040, 4, 4)]
    [InlineData(3440, 1400, 12, 3)]
    [InlineData(101, 97, 24, 24)]
    public void Every_pixel_maps_to_the_cell_whose_bounds_contain_it(int width, int height, int columns, int rows)
    {
        var area = new RectI(-300, 20, -300 + width, 20 + height);
        var layout = new GridLayout(area, columns, rows);

        for (int x = area.Left; x < area.Right; x++)
        {
            var cell = layout.CellAt(new PointI(x, area.Top));
            Assert.True(layout.CellBounds(CellSpan.Single(cell)).Contains(new PointI(x, area.Top)), $"x={x}");
        }

        for (int y = area.Top; y < area.Bottom; y++)
        {
            var cell = layout.CellAt(new PointI(area.Left, y));
            Assert.True(layout.CellBounds(CellSpan.Single(cell)).Contains(new PointI(area.Left, y)), $"y={y}");
        }
    }

    [Fact]
    public void Points_outside_the_area_map_to_the_nearest_cell()
    {
        var layout = new GridLayout(Area, 4, 4);

        Assert.Equal(new Cell(0, 0), layout.CellAt(new PointI(-500, -500)));
        Assert.Equal(new Cell(3, 3), layout.CellAt(new PointI(5000, 5000)));
    }

    [Fact]
    public void Neighbouring_cells_share_an_edge_without_gaps()
    {
        var layout = new GridLayout(new RectI(0, 0, 1366, 768), 7, 5);

        for (int c = 0; c < 6; c++)
        {
            Assert.Equal(
                layout.WindowBounds(new CellSpan(c, 0, c, 0)).Right,
                layout.WindowBounds(new CellSpan(c + 1, 0, c + 1, 0)).Left);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(7)]
    public void Window_gap_separates_neighbours_by_exactly_the_gap(int gap)
    {
        var layout = new GridLayout(Area, 4, 4, windowGap: gap);

        var left = layout.WindowBounds(new CellSpan(0, 0, 0, 0));
        var right = layout.WindowBounds(new CellSpan(1, 0, 1, 0));
        var below = layout.WindowBounds(new CellSpan(0, 1, 0, 1));

        Assert.Equal(gap, right.Left - left.Right);
        Assert.Equal(gap, below.Top - left.Bottom);
    }

    [Fact]
    public void Window_gap_does_not_apply_at_the_screen_edge()
    {
        var layout = new GridLayout(Area, 4, 4, windowGap: 10);

        var all = layout.WindowBounds(new CellSpan(0, 0, 3, 3));

        Assert.Equal(Area, all);
    }

    [Fact]
    public void Edge_gap_applies_only_at_the_screen_edge()
    {
        var layout = new GridLayout(Area, 4, 4, windowGap: 0, edgeGap: 12);

        var corner = layout.WindowBounds(new CellSpan(0, 0, 0, 0));
        var all = layout.WindowBounds(new CellSpan(0, 0, 3, 3));

        Assert.Equal(new RectI(12, 12, 480, 260), corner);
        Assert.Equal(new RectI(12, 12, 1908, 1028), all);
    }

    [Fact]
    public void Gaps_larger_than_a_cell_are_ignored_instead_of_inverting_the_window()
    {
        var layout = new GridLayout(new RectI(0, 0, 200, 200), 10, 10, windowGap: 200, edgeGap: 200);

        var bounds = layout.WindowBounds(new CellSpan(4, 4, 4, 4));

        Assert.False(bounds.IsEmpty);
        Assert.Equal(new RectI(80, 80, 100, 100), bounds);
    }

    [Fact]
    public void Span_corners_can_be_given_in_any_order()
    {
        var span = CellSpan.FromCorners(new Cell(3, 2), new Cell(1, 0));

        Assert.Equal(new CellSpan(1, 0, 3, 2), span);
        Assert.Equal(3, span.Columns);
        Assert.Equal(3, span.Rows);
    }

    [Fact]
    public void Snap_origin_picks_the_nearest_grid_position()
    {
        var layout = new GridLayout(Area, 4, 4);

        Assert.Equal(new PointI(480, 260), layout.SnapOrigin(new PointI(500, 300)));
        Assert.Equal(new PointI(0, 0), layout.SnapOrigin(new PointI(-40, 100)));
        Assert.Equal(new PointI(1440, 780), layout.SnapOrigin(new PointI(4000, 4000)));
    }

    [Fact]
    public void Snap_origin_respects_gaps()
    {
        var layout = new GridLayout(Area, 4, 4, windowGap: 10, edgeGap: 20);

        Assert.Equal(new PointI(20, 20), layout.SnapOrigin(new PointI(0, 0)));
        Assert.Equal(new PointI(485, 265), layout.SnapOrigin(new PointI(470, 270)));
    }

    [Fact]
    public void Grid_never_has_more_cells_than_pixels()
    {
        var layout = new GridLayout(new RectI(0, 0, 3, 2), 10, 10);

        Assert.Equal(3, layout.Columns);
        Assert.Equal(2, layout.Rows);
    }

    [Theory]
    [InlineData(4, 4, 0, 4, 4)]
    [InlineData(4, 4, 1, 8, 8)]
    [InlineData(4, 4, 3, 16, 16)]
    [InlineData(4, 4, -1, 2, 2)]
    [InlineData(1, 3, -1, 1, 2)]
    [InlineData(6, 4, 99, 24, 16)]
    public void Density_scales_the_grid(int columns, int rows, int level, int expectedColumns, int expectedRows)
    {
        Assert.Equal((expectedColumns, expectedRows), GridDensity.Apply(columns, rows, level));
    }
}
