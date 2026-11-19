// The possession view's visible tiles (WorldScene.PossessionVisibility.cs): a terrain fragment of a hidden tile is not drawn.
global uniform bool dk2_possession_visibility_enabled;
global uniform sampler2D dk2_possession_tiles;
global uniform ivec2 dk2_possession_grid;
varying flat int dk2_possession_tile;
bool dk2_possession_tile_visible(int tile) {
    if (!dk2_possession_visibility_enabled) return true;
    int width = dk2_possession_grid.x;
    if (tile < 0 || tile >= width * dk2_possession_grid.y) return false;
    return texelFetch(dk2_possession_tiles, ivec2(tile % width, tile / width), 0).r > 0.0;
}
