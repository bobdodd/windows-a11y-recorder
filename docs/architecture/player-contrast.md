# Colours and contrast in the recorder's window

The owner found, on 2026-11-10, that text in the Accessibility tree and
the properties panel was hard to read, and that "there are contrast
issues throughout the UI". This is the audit of the recorder's main
window, and of the windows the tabs open into, made the same day, and
what was changed. The owner chose to do it before more Android work,
and to include the menu bar, which the owner had asked earlier to be
"much more visible".

The thresholds are WCAG 2.2's: 4.5 to 1 for text (1.4.3), and 3 to 1
for what identifies a control and its state, such as its edges, and for
graphics needed to understand the content (1.4.11). Inactive controls,
such as a disabled button, are exempt from both. Every ratio here is
computed from the colours with WCAG's formula for relative luminance.

## The cause

The window is dark: #171614, with panels of #1C1B19, and text #CDCCCA
(10.7 to 1 on the panels). Its lists, its tree, its properties grid and
its text boxes are Windows' own controls, which keep Windows' white
background and set Windows' dark text colour.

The window's styles have given every TextBlock the colour #CDCCCA since
the first build. WPF does not apply such a style to a TextBlock inside a
list's or tree's row templates, so the rows' text kept the controls'
dark colour. On 2026-10-14, commit 208af19 ("Text in the player can be
selected and copied") made that text SelectableText, a kind of text box,
and gave SelectableText the same colour. WPF does apply a style for a
control inside templates, so the colour then reached the text inside
the white controls and overrode the dark colour they set. The owner's
screenshots of 2026-11-10 measure that text at (205, 204, 202) on
(255, 255, 255): 1.6 to 1. How WPF applies these styles is from its
documented behaviour, not measured here; the colours are measured.

## What was checked, and what changed

| Where | Colours | Before | After |
|---|---|---|---|
| Text in the white lists, tree, properties grid | #CDCCCA on white | 1.6 | Windows' text colour, black on white: 21 |
| The tree's second lines | #4A4945 on white | 9.0 | unchanged |
| A selected row in the tree | black on #E6E0F0 | 16.3 | unchanged |
| A selected row in the tree, against the rows round it | #E6E0F0 on white, no edge | 1.29 | an edge of #3D2D5C, 2 wide: 9.4 on #E6E0F0, 12.2 on white (2026-11-12, owner: "it fails non-text contrast of 3:1") |
| The purple bar of the reading position and accessibility focus rows | #6A4C93 on white | 6.9 | unchanged |
| Text on the window and panels | #CDCCCA on #171614, #1C1B19 | 11.3, 10.7 | unchanged |
| Muted text (notes, help lines) | #A09F9B on #1C1B19, #171614 | 6.5, 6.8 | unchanged |
| Tabs' text, unselected and selected | #CDCCCA on #201F1D, #2A2926 | 10.3, 9.1 | unchanged |
| Selected tab's bar | #B79CE6 on #2A2926 | 6.2 | unchanged |
| Tabs' edges | #75736F on #171614 | 3.8 | unchanged |
| Group boxes' and the side panel's edges | #393836 on #1C1B19 | 1.5 | #75736F: 3.6 |
| Splitters between panels | #393836 on #1C1B19 | 1.5 | #75736F: 3.6 |
| The splitter between the tabs and Selected event values | transparent | none | as the other splitters: 3.6 |
| A focused splitter or tab edge | Windows' highlight, #0078D7 by default, on #1C1B19 | 3.8 | unchanged |
| The menu bar | #1C1B19 on the window's #171614, no edge | not told apart | #2A2926 with an edge of #75736F under it: 3.8 against the window |
| The menus' titles | #CDCCCA on #1C1B19, no edge | 10.7, no edge | #CDCCCA on #2A2926, 9.1, semibold, each in a box with an edge of #75736F (3.1 against the bar) |
| The menus' items | Windows' menu colours | Windows' | unchanged, in normal weight |
| TalkBack's utterances in the timeline | #2F6F73 on the timeline's #201F1D; white text on it | 2.9; 5.8 | #2B787C: 3.2; 5.2 |
| The reading position in the timeline | #6A4C93 on #201F1D; white text on it | 2.4; 6.9 | #7A5CAB: 3.1; 5.3 |
| NVDA's windows in the timeline | #3F6B3A on #201F1D; white text on it | 2.6; 6.2 | #467A40: 3.2; 5.1 |
| NVDA's browse mode in the timeline | #4F6F96 on #201F1D; white text on it | 3.2; 5.2 | unchanged |
| NVDA's focus mode in the timeline | #C9A05A on #201F1D; dark text on it | 6.8; 6.8 | unchanged |
| The timeline's marks, per channel | #4F98A3, #5591C7, #FDAB43, #A86FDF, #797876, #DD6974, #6DAA45, #BAB9B4 on #201F1D | 3.7 to 8.7 | unchanged |
| Buttons | Windows' colours, black on #DDDDDD | 15.5 | unchanged |

The colours of the timeline's spans changed only as far as needed to
reach 3 to 1 against the timeline with their white text still above 4.5
to 1. Each still has its text, and the interrupted and uncertain ones
still have stripes, so none relies on colour alone.

## The fix

- The window's styles no longer give TextBlock or SelectableText a
  colour. Text takes the colour of what it is in: the window's #CDCCCA
  on the dark panels, and Windows' own dark text colour in the white
  controls, which set it. Labels and check boxes keep their colour, as
  Windows' styles for them set a dark one, which on the dark panels
  would be the same fault the other way round.
- The window's styles say outright that lists, list views and trees take
  Windows' text colour, so their text stays dark whatever they are in.
- The edges' colour, `PanelBorder`, and the splitters' colour are
  #75736F, the colour the tabs' edges already had.
- The menu bar is lighter than the window, with an edge under it, and
  each menu's title is semibold in a box with an edge, as a button is.
  Its items keep Windows' menu colours, in normal weight.
- The timeline's TalkBack, reading position and NVDA window spans have
  the colours in the table.

A tab opened in its own window takes the main window's styles, so the
same fixes apply there.

## Not covered

- **The other windows.** Setting up models, model profiles, the
  report, the participant's capabilities, MP4 export, Android setup and
  recorded documents are built in code without colours of their own, so
  they take Windows' colours throughout. They were not changed.
- **Windows' contrast themes.** The dark panels and the timeline have
  fixed colours, so a Windows contrast theme does not change them; the
  white controls follow it. Following a contrast theme throughout is a
  larger change and was not asked for.
- **Lists with content in a desktop recording.** The list of NVDA's
  announcements, the reading position's steps and the browser's
  navigations are lists like the tree and take the same fix, but a
  recording of the phone leaves them empty, so the tests below show
  them only as they are without content.

## Checks

The solution builds with no warnings. The colours can only be seen on
Windows, so the [contrast validation plan](../validation/player-contrast-plan.md)
has the tests on the development PC. The owner checked the updated
window on 2026-11-11 and accepted it; no screenshots were sent, so the
ratios on screen were not measured.
