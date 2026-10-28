# 1.3.0 更新说明 / Update notes

（1.3.0 = 1.2.x 的全部内容 + 窗户光照重做 / 1.3.0 is 1.2.x plus the rebuilt window daylight）

## 中文

```
1.3.0 更新
· 修复屋顶平台/门廊被额外点亮的问题：窗户和半墙还是按 1.2 的方式把天光透进屋里，
  但"露天屋顶边缘"不再自动补光——柱撑平台回到原版亮度（约 0.63 倍，而不是 1.17 倍）
· 窗透进来的光取自它朝外那一侧：露天是天光（会随天气变暗），夜里也可以是外面的灯；
  不和屋里的人造光叠加、两扇窗也不叠加（取较亮）
· 新增"模组设置"：补光刷新间隔（0.5–20 秒）和总开关
· 更新简体中文
· 修复若干 bug（带窗半墙被画成整墙、窗户不跟所选材质、窗与两侧墙之间的缝隙等）
· 去除 "Fit window" 按钮
· 更新半墙图标（按材质分面）
· 半墙现在可以像墙一样连续拖拽建造
· 修复半墙脚下那条草地过渡带：半墙现在和原版墙一样设置 coversFloor，地板直接铺到墙根（掩体/光照不变）
· 窗透进来的光不再被画面颜色抬高：室外 20% 时屋里最多 20%；两扇窗、窗和灯之间也不再叠加（取最大值）
```

## English

```
1.3.0 update
- Fixed roofed platforms and porches being lit brighter than the world: windows and half walls still pass the daylight
  in (the 1.2 way), but a bare roof edge no longer gets light of its own - a platform on pillars is back to vanilla
  (about 0.63x, not 1.17x).
- The light a window passes comes from its sky side: the open sky (dimmed by weather) or, at night, a lamp out there.
  It does not add to the room's lamps and two windows do not stack - the brighter one wins.
- New Mod settings: how often the light is refreshed (0.5-20 s) and an off switch.
- Simplified Chinese added
- Several bug fixes (glazed half wall drawn as a full wall, windows ignoring the chosen material, a gap between the
  window and the walls beside it, etc.)
- Removed the "Fit window" button
- New half wall icon (face follows the material)
- Half walls can now be drag-built in a row, like walls
- Fixed the grass band at the foot of a half wall: half walls now set coversFloor like a vanilla wall, so the floor runs right up to them (cover and light unchanged)
- Window light no longer reads brighter than the sky: 20% outside means 20% at most inside, and two windows or a window and a lamp no longer add up (the brighter one wins)
```

需要 RimWorld 1.7 + Harmony + IsoRim Waylith + IsoRim ProjectZ。
Requires RimWorld 1.7, Harmony, IsoRim Waylith and IsoRim ProjectZ.
