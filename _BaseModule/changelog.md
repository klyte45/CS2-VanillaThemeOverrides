# v2.0.0r1 (26-JUL-25)

- Fixed issue relative to abbreviations files
- Fixing transparency of floor decals
- Added importable street sign layout, allowing to import the street sign manually in any game node prop

## FROM  v2.0.0r0 (22-JUL-25)
- Now hiding original meshes from the road decals
- Added support to customizable street signs:
  - They place themselves at center of the corner and point to the right direction of the road they sign to.
  - By default, the street signs props are replaced by the custom model, but it can be added manually to other props.
  - It's not recommended to use the custom sign layout in a corner that another prop already uses that layout, or the custom layouts will overlap.
  - It's not advised to add the custom sign layouts to other prefabs, as it may cause issues with the original street signs causing overlaps.
  - The custom sign layouts only will work on props that are sub-objects of a node. The corner position is calculated based on the azimuth angle position compared to street directions.
  - The custom sign layout can receive two layouts replacement, one for each sign on the pole. The two can be the same.
  - The primary sign is related to the edge lane the prop holding the layout was designed to represent, while the secondary sign is related to the side edge lane.
- Added support to abbreviations. They are used to create the short name line at default Brazilian model of street signs.