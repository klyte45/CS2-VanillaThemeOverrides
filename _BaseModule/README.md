# Vanilla Theme Override - Brazil/Customizable

This Write Everywhere Module targets to override all transit props from the base vanilla themes (North America and Europe) into a customizable setup,
defaulting to Brazilian style.

To customize and create your own style:
- At WE window, export the Brazil image atlas;
- Go to exported folder and edit your images;
- Ensure the exported folder is now inside `imageAtlas` folder at WE mod data folder;
- Reload the sprites ingame/restart the game;
- at Templates Setup tab, search by this mod in the list and select your created atlas to replace the original.

The original PSD files for some images are available at GitHub.

Since 2.0, was added a feature that replaces the default street sign with actual naming signs:

- They place themselves at center of the corner and point to the right direction of the road they sign to.
- By default, the street signs props are replaced by the custom model, but it can be added manually to other props.
- It's not recommended to use the custom sign layout in a corner that another prop already uses that layout, or the custom layouts will overlap.
- It's not advised to add the custom sign layouts to other prefabs, as it may cause issues with the original street signs causing overlaps.
- The custom sign layouts only will work on props that are sub-objects of a node. The corner position is calculated based on the azimuth angle position compared to street directions.
- The custom sign layout can receive two layouts replacement, one for each sign on the pole. The two can be the same.
- The primary sign is related to the edge lane the prop holding the layout was designed to represent, while the secondary sign is related to the side edge lane.

To calculate the right position, it tries to find the pedestrian path nearer to the middle of the two edges that composes the corner. It have a weird behavior on pedestrian roads without tram upgrade since the pedestrians walk on the center of the road... But in the end this is the correct position due the caracteristics of the road.

Also was added an abbreviations file feature, similar that was available before for Write The Signs/Write Everywhere for Cities Skylines 1. This implementation accepts regex and have some special behaviors. 

Sample files are available for pt-br and en-us locales at mod GitHub page. Just copy their contents on the locally generated file and the game will load it automatically. There are buttons for both GitHub samples and the local file at Options menu inside this mod tab.

