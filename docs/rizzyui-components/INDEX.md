# RizzyUI Components Reference

> Fetched from https://rizzyui.jalex.io/components.html on 2026-07-18
> This is a comprehensive component reference with parameters & Alpine APIs for AI agent consumption.

---

## Accordion (`RzAccordion`)
**File:** `accordion.html`

### RzAccordion Parameters
| Property | Type | Default |
|---|---|---|
| `Type` | `AccordionType` (Multiple, Single) | `Multiple` |
| `ChildContent` | `RenderFragment` | `null` |

### AccordionItem Parameters
| Property | Type | Default |
|---|---|---|
| `Title` | `string` | `""` |
| `Collapsed` | `bool` | `true` |
| `AccordionTrigger` | `RenderFragment?` | `null` |
| `AccordionContent` | `RenderFragment?` | `null` |

### Alpine: `rzAccordion` + `accordionItem`
No public methods; driven by markup.

---

## Alert (`RzAlert`)
**File:** `alert.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Variant` | `ThemeVariant` | `Information` |
| `Icon` | `SvgIcon?` | `null` (auto per variant) |
| `Dismissable` | `bool` | `false` |
| `LiveRegionMode` | `RzAlertLiveRegionMode` (Alert, Status) | `Alert` |
| `Pulse` | `bool` | `false` |
| `ChildContent` | `RenderFragment?` | `null` |

### Sub-components: `AlertTitle`, `AlertDescription` (ChildContent only)

### Alpine: `rzAlert`
- `dismiss()` — Dismisses alert

---

## Alert Dialog (`RzAlertDialog`)
**File:** `alert-dialog.html`

### RzAlertDialog Parameters
| Property | Type | Default |
|---|---|---|
| `CloseOnEscape` | `bool` | `false` |
| `CloseOnClickOutside` | `bool` | `false` |

### AlertDialogContent Parameters
| Property | Type | Default |
|---|---|---|
| `Size` | `ModalSize` | `Large` |

---

## Aspect Ratio (`RzAspectRatio`)
**File:** `aspect-ratio.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Ratio` | `double` (w/h) | `1` |
| `ChildContent` | `RenderFragment?` | `null` |

### Alpine: `rzAspectRatio` — No public methods

---

## Aurora Text (`RzAuroraText`)
**File:** `aurora-text.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment?` | `null` |
| `Colors` | `string[]?` | `["#FF0080","#7928CA","#0070F3","#38bdf8"]` |
| `Speed` | `double` | `1` |
| `AriaLabel` | `string?` | `null` |
| `Decorative` | `bool` | `false` |

CSS-only, no Alpine API.

---

## Avatar (`RzAvatar`)
**File:** `avatar.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Shape` | `AvatarShape` (Circle, Square) | `Circle` |
| `Size` | `Size` | `Medium` |
| `Border` | `bool` | `false` |
| `AriaLabel` | `string?` | `null` |
| `ChildContent` | `RenderFragment?` | `null` |

### Sub-components: `AvatarImage` (ImageSource, AlternateText), `AvatarFallback`, `AvatarBadge`, `AvatarGroup`, `AvatarGroupCount`

---

## Badge (`RzBadge`)
**File:** `badge.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Variant` | `ThemeVariant` | `Default` |
| `Soft` | `bool` | `false` |
| `Icon` | `SvgIcon?` | `null` |
| `Label` | `string` | `""` |
| `ChildContent` | `RenderFragment?` | `null` |

---

## Breadcrumb (`RzBreadcrumb`)
**File:** `breadcrumb.html`

### RzBreadcrumb Parameters
| Property | Type | Default |
|---|---|---|
| `AriaLabel` | `string?` | `null` |
| `ChildContent` | `RenderFragment?` | `null` |

### BreadcrumbLink Parameters
| Property | Type | Default |
|---|---|---|
| `Href` | `string?` | `null` |
| `ChildContent` | `RenderFragment?` | `null` |

### Sub-components: `BreadcrumbList`, `BreadcrumbItem`, `BreadcrumbSeparator`, `BreadcrumbPage`, `BreadcrumbEllipsis`

---

## Button (`RzButton`)
**File:** `buttons.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Variant` | `ThemeVariant` | `Primary` |
| `Size` | `Size` | `Medium` |
| `Outline` | `bool` | `false` |
| `Animate` | `bool` | `true` |
| `Label` | `string` | `""` |
| `ChildContent` | `RenderFragment` | `null` |
| `AssistiveLabel` | `string` | `"Action Button"` |

---

## Button Group (`RzButtonGroup`)
**File:** `button-group.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment` | Required |
| `Orientation` | `Orientation` | `Horizontal` |
| `GroupVariant` | `ThemeVariant?` | `null` |

### `ButtonGroupSeparator`: `Orientation` (`Vertical`)

---

## Calendar (`RzCalendar`)
**File:** `calendar.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Mode` | `SelectionDatesMode` | `Single` |
| `Value` | `DateOnly?` | `null` |
| `Values` | `List<DateOnly>?` | `null` |
| `Range` | `CalendarDateRange?` | `null` |
| `MinDate` | `DateOnly?` | `null` |
| `MaxDate` | `DateOnly?` | `null` |
| `Type` | `CalendarType` | `Default` |
| `Options` | `VanillaCalendarOptions` | `null` |

### Events: `rz:calendar:init`, `rz:calendar:click-day`, `rz:calendar:change-time`, `rz:calendar:click-month`, `rz:calendar:click-year`

### Alpine: `rzCalendar`
- `initCalendar(configId)` — Inits Vanilla Calendar
- `dispatchCalendarEvent(eventName, detail)` — Dispatches custom events

---

## Calendar Provider (`RzCalendarProvider`)
**File:** `calendar-provider.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment` | Required |
| `Mode` | `SelectionDatesMode` | `Single` |
| `Value` | `DateOnly?` | `null` |
| `Values` | `List<DateOnly>?` | `null` |
| `Range` | `CalendarDateRange?` | `null` |
| `Locale` | `string?` | `null` |
| `FormatOptions` | `IntlDateTimeFormatOptions?` | `null` |

### Alpine: `rzCalendarProvider`
- `setToday()`, `addDays(n)`, `setDate(dateStr)`, `clear()`, `toggleDate(dateStr)`, `syncToCalendar()`

---

## Chart (`RzChart`)
**File:** `chart.html`, `chart-bar.html`, `chart-line.html`

Fluent C# API using Chart.js. Configure via `.Data(d => d.Labels(...).Datasets(ds => ds.Bar() / ds.Line()...))`.

---

## Card (`RzCard`)
**File:** `card.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment?` | `null` |

### Sub-components: `CardHeader`, `CardTitle`, `CardSubtitle` (via CardDescription), `CardAction`, `CardContent`, `CardFooter`

---

## Carousel (`RzCarousel`)
**File:** `carousel.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment` | Required |
| `Options` | `CarouselOptions` | `new()` |
| `Plugins` | `IEnumerable<EmblaPlugin>` | Empty |
| `AriaLabel` | `string?` | `"Carousel"` |

### CarouselOptions common props: `Align`, `Axis` ("x"/"y"), `Loop`, `StartIndex`, `DragFree`

### Alpine: `rzCarousel`
- `scrollPrev()`, `scrollNext()`, `scrollTo(index)`

---

## Checkbox / Checkbox Group (`RzCheckbox`, `RzCheckboxGroup`)
**File:** `checkbox.html`

### RzInputCheckbox: `For` (Expression<Func<bool>>)

### RzCheckboxGroup<TValue>
| Property | Type | Default |
|---|---|---|
| `For` | `Expression<Func<IList<TValue>>>` | Required |
| `ChildContent` | `RenderFragment` | Required |
| `Name` | `string?` | `null` |

### RzCheckboxGroupItem: `Value` (TValue), `ChildContent`

---

## Clipboard (`RzClipboard`)
**File:** `clipboard.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment` | Required |
| `Value` | `string?` | `null` |
| `TargetSelector` | `string?` | `null` |
| `PreferValue` | `bool` | `false` |
| `FeedbackDuration` | `int` | `1200` |
| `Disabled` | `bool` | `false` |
| `UseFallback` | `bool` | `true` |
| `AriaLabel` | `string?` | `"Copy to clipboard"` |
| `AsChild` | `bool` | `false` |

### Sub-components: `ClipboardTrigger`, `ClipboardFeedback`

### Alpine: `rzClipboard`
- `copy()`, `getTextToCopy()`, `fallbackCopy(text)`
- Events: `rz:copy`, `rz:copy-failed`

---

## Collapsible (`RzCollapsible`)
**File:** `collapsible.html`

### RzCollapsible: `ChildContent` (required), `DefaultOpen` (false)
### CollapsibleTrigger: `ChildContent` (required), `AsChild` (false)
### CollapsibleContent: `ChildContent` (required)

### Alpine: `rzCollapsible`
- `toggle()`, `state()` → "open"/"closed"

---

## Back To Top (`RzBackToTop`)
**File:** `back-to-top.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Threshold` | `int` | `300` |
| `Variant` | `ThemeVariant` | `Default` |
| `Size` | `Size` | `Medium` |
| `Outline` | `bool` | `false` |
| `AriaLabel` | `string?` | `"Back to top"` |
| `AsChild` | `bool` | `false` |

### Alpine: `rzBackToTop`
- `scrollToTop()`, `destroy()`

---

## Color Picker (`RzColorPickerProvider`)
**File:** `color-picker.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Value` | `string?` | `null` |
| `Format` | `ColorFormat` | `Hex` |
| `Alpha` | `bool` | `false` |
| `Swatches` | `IEnumerable<string>?` | `null` |
| `SwatchesOnly` | `bool` | `false` |
| `CloseButton` | `bool` | `false` |
| `ClearButton` | `bool` | `false` |

### Alpine: `rzColorPickerProvider`
- Exposes `colorPicker.value`, `colorPicker.open`, `colorPicker.setValue(value)`, `colorPicker.getValue()`, `colorPicker.updateConfiguration(config)`

---

## Input Color (`RzInputColor`)
**File:** `input-color.html`

Turnkey color input built from ColorPickerProvider + ColorSwatch.

### Parameters: `For`, `Value`, `Swatches`, `CloseButton`, `ClearButton`

---

## Color Swatch (`RzColorSwatch`)
**File:** `color-swatch.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Value` | `string?` | `null` |
| `Size` | `ColorSwatchSize` | `Default` |
| `WithoutTransparency` | `bool` | `false` |
| `Disabled` | `bool` | `false` |
| `AriaLabel` | `string?` | `null` |

### Alpine: `rzColorSwatch` — `getValue()`, `setValue(value)`

---

## Combobox (`RzCombobox`)
**File:** `combobox.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Items` | `IEnumerable<TItem>` | Empty |
| `Options` | `ComboboxOptions` | `new()` |
| `Multiple` | `bool` | `false` |
| `Placeholder` | `string?` | Localized |
| `ValueSelector` | `Func<TItem, string>?` | Auto |
| `TextSelector` | `Func<TItem, string>?` | Auto |
| `OptionTemplate` | `RenderFragment?` | `null` |
| `ItemTemplate` | `RenderFragment?` | `null` |

### Alpine: `rzCombobox`
- State: `value`, `values`, `text`, `texts`, `tomSelect`
- `initTomSelect()`, `syncSelectedState(emit)`, `getOptionText(value)`
- Event: `rz:combobox:change`

---

## Confetti (`RzConfetti`)
**File:** `confetti.html`

### Parameters
| Property | Type | Notes |
|---|---|---|
| `Trigger` | `ConfettiTrigger` | Click, Hover, Load, Visible, CustomEvent, Manual |
| `Mode` | `ConfettiMode` | Overlay, Scoped |
| `Preset` | `ConfettiPreset` | DefaultBurst, CannonLeft/Right, DualCannons, Victory, Subtle, Stars |
| `BurstCount` | `int` | Burst sequence count |
| `BurstIntervalMs` | `int` | Burst cadence |
| `DisableForReducedMotion` | `bool` | `true` |

---

## Command (`RzCommand`)
**File:** `command.html`

### RzCommand Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment` | Required |
| `Items` | `IEnumerable<object>` | Empty |
| `ItemsUrl` | `string?` | `null` |
| `ServerFiltering` | `bool` | `false` |
| `ShouldFilter` | `bool` | `true` |
| `Loop` | `bool` | `false` |
| `SelectedValue` | `string?` | `null` |
| `MaxRender` | `int` | `100` |

### RzCommandDialog: `Open`, `EventTriggerName`, `Title`, `Description`, `ShowCloseButton`
### CommandItem: `Value`, `Keywords`, `Disabled`, `ForceMount`

### Alpine: `rzCommand` — `handleInteraction()`

---

## Dialog (`RzDialog`)
**File:** `dialog.html`

### RzDialog Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment` | Required |
| `EventTriggerName` | `string` | `""` |
| `CloseEventName` | `string` | `Constants.Events.DialogClose` |
| `CloseOnEscape` | `bool` | `true` |
| `CloseOnClickOutside` | `bool` | `true` |

### DialogContent: `ShowCloseButton`, `Size` (ModalSize), `AriaLabel`

### Alpine: `rzModal`
- `openModal(event?)`, `closeModal()`, `handleClickOutside(event)`

---

## Dropdown Menu (`RzDropdownMenu`)
**File:** `dropdown.html`

### RzDropdownMenu Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment` | Required |
| `Anchor` | `AnchorPoint` | `Bottom` |
| `Offset` | `int` | `4` |
| `IsModal` | `bool` | `false` |

### DropdownMenuItem: `Icon`, `ShortcutContent`, `Disabled`
### DropdownMenuSub: `ChildContent`, `Anchor` (RightStart), `Offset` (-4)

### Alpine: `rzDropdownMenu` — `toggle()`, `updatePosition()`

---

## Event Viewer (`RzEventViewer`)
**File:** `event-viewer.html`

Compact event log. `EventName` parameter to filter events. No Alpine API.

---

## Empty (`RzEmpty`)
**File:** `empty.html`

### Parameters: `AriaLabel`, `ChildContent`
### Sub-components: `EmptyHeader`, `EmptyMedia`, `EmptyTitle`, `EmptyDescription`, `EmptyContent`

---

## File Input (`RzFileInput`)
**File:** `file-input.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Name` | `string?` | `null` |
| `Multiple` | `bool` | `false` |
| `Accept` | `string?` | `null` |
| `Variant` | `FileInputVariant` | `Default` (Button, Dropzone) |
| `Disabled` | `bool` | `false` |
| `AriaLabel` | `string?` | `"File input"` |
| `For` | `Expression<Func<object?>>?` | `null` |

---

## Input (`RzInputText`, `RzInputNumber`)
**File:** `input.html`

### RzInputText: `For` (required), `Role` (TextRole), `Placeholder`
### RzInputNumber: `For` (required), `TValue`

### RzInputGroup sub-components: `InputGroupInput`, `InputGroupAddon`, `InputGroupText`, `InputGroupButton`, `InputGroupTextarea`

---

## Input OTP (`RzInputOTP`)
**File:** `input-otp.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Length` | `int` | Required |
| `Name` | `string?` | `null` |
| `Value` | `string?` | `null` |
| `OtpType` | `InputOtpType` | `Numeric` |
| `Disabled` | `bool` | `false` |
| `Required` | `bool` | `false` |
| `Invalid` | `bool?` | `null` |
| `TextTransform` | `InputOtpTextTransform` | `None` |

### Sub-components: `InputOTPSlot` (Index), `InputOTPGroup`, `InputOTPSeparator`

### Alpine: `rzInputOTP` — `getValue()`, `setValue(newValue)`, events: `rz:inputotp:oninput`, `rz:inputotp:onchange`

---

## Item (`RzItem`)
**File:** `item.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Variant` | `ItemVariant` (Default, Outline, Muted) | `Default` |
| `Size` | `Size` | `Medium` |
| `AsChild` | `bool` | `false` |

### Sub-components: `ItemMedia`, `ItemContent`, `ItemTitle`, `ItemDescription`, `ItemActions`, `ItemHeader`, `ItemFooter`

---

## Kbd (`RzKbd`, `RzKbdGroup`)
**File:** `kbd.html`

Simple keyboard key display. `ChildContent` only. `RzKbdGroup` wraps multiple `RzKbd`.

---

## Highlighter (`RzHighlighter`)
**File:** `highlighter.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment?` | `null` |
| `Action` | `HighlighterAction` | `Highlight` (Highlight, Underline) |
| `Color` | `string?` | `var(--highlight-1)` |
| `StrokeWidth` | `double` | `1.5` |
| `AnimationDuration` | `int` | `600` |
| `Iterations` | `int` | `2` |
| `Multiline` | `bool` | `true` |
| `StartOnView` | `bool` | `false` |

### Alpine: `rzHighlighter` — No public events. Safe in HTMX regions.

---

## Link (`RzLink`)
**File:** `link.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Href` | `string` | `"#"` |
| `Underline` | `bool` | `true` |
| `ChildContent` | `RenderFragment?` | `null` |

---

## Markdown (`RzMarkdown`)
**File:** `markdown.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Content` | `RenderFragment?` | `null` |
| `ComponentAssets` | `string[]` | `null` |
| `ProseWidth` | `ProseWidth` | `Comfortable` |
| `Pipeline` | `MarkdownPipeline?` | `null` |

### Alpine: `rzMarkdown` — Auto init only

---

## Marquee (`RzMarquee`)
**File:** `marquee.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Orientation` | `MarqueeOrientation` | `Horizontal` |
| `Reverse` | `bool` | `false` |
| `PauseOnHover` | `bool` | `false` |
| `Repeat` | `int` | `4` |
| `Duration` | `string` | `"40s"` |
| `Gap` | `string` | `"1rem"` |
| `ChildContent` | `RenderFragment?` | `null` |

CSS-only, no Alpine API.

---

## Menubar (`RzMenubar`)
**File:** `menubar.html`

### Sub-components: `MenubarMenu`, `MenubarTrigger`, `MenubarContent`, `MenubarItem` (Icon, Variant), `MenubarSeparator`, `MenubarLabel`, `MenubarShortcut`, `MenubarCheckboxItem`, `MenubarRadioGroup`, `MenubarRadioItem`, `MenubarSub`, `MenubarSubTrigger`, `MenubarSubContent`

### Alpine: `rzMenubar`
- `openMenu(value, trigger)`, `closeMenus()`, `handleTriggerKeydown(event)`, `toggleCheckboxItem(event)`, `selectRadioItem(event)`
- `rzMenubarSubmenu`: `openSubmenu()`, `closeSubmenu()`, `forceCloseSubmenu()`, `focusNextItem()`, `focusPreviousItem()`

---

## Navigation Menu (`RzNavigationMenu`)
**File:** `navigation-menu.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment?` | `null` |
| `Orientation` | `Orientation` | `Horizontal` |
| `AriaLabel` | `string?` | `null` |

### Sub-components: `NavigationMenuList`, `NavigationMenuItem`, `NavigationMenuTrigger`, `NavigationMenuContent`, `NavigationMenuLink`

### Alpine: `rzNavigationMenu` — `openMenu(id)`, `closeMenu()`

---

## Native Select (`RzNativeSelect`)
**File:** `native-select.html`

### Parameters: `For` (required), `Value`, `ChildContent`, `AriaDescribedBy`
### Sub-components: `RzNativeSelectOption`, `RzNativeSelectOptGroup`

---

## Number Ticker (`RzNumberTicker`)
**File:** `number-ticker.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Value` | `decimal` | Required |
| `StartValue` | `decimal` | `0` |
| `Direction` | `NumberTickerDirection` | `Up` |
| `Delay` | `double` | `0` |
| `DecimalPlaces` | `int` | `0` |
| `Culture` | `string?` | Current |
| `TriggerOnView` | `bool` | `true` |
| `AnimateOnce` | `bool` | `true` |
| `DisableAnimation` | `bool` | `false` |

### Alpine: `rzNumberTicker` — No public methods

---

## Popover (`RzPopover`)
**File:** `popover.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `PopoverTrigger` | `RenderFragment` | Required |
| `PopoverContent` | `RenderFragment` | Required |
| `Anchor` | `AnchorPoint` | `BottomStart` |
| `Offset` | `int` | `4` |
| `CrossAxisOffset` | `int` | `0` |
| `EnableFlip` | `bool` | `true` |
| `EnableShift` | `bool` | `true` |
| `AriaLabel` | `string?` | `null` |

### Alpine: `rzPopover` — `toggle()`, `updatePosition()`

---

## Pagination (`RzPagination`)
**File:** `pagination.html`

### RzPagination: `AriaLabel`, `ChildContent`
### PaginationLink: `Href`, `IsActive`, `AriaLabel`, `ChildContent`
### PaginationPrevious/PaginationNext: `Href`, `Label`, `AriaLabel`
### Sub-components: `PaginationList`, `PaginationItem`, `PaginationEllipsis`

---

## Progress (`RzProgress`)
**File:** `progress.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `CurrentValue` | `int` | `0` |
| `MinValue` | `int` | `0` |
| `MaxValue` | `int` | `100` |
| `Label` | `string?` | `null` (supports `{percent}`) |
| `LabelPosition` | `ProgressLabelPosition` | `Outside` |
| `Variant` | `StatusColor` | `Primary` |
| `AriaLabel` | `string` | `"Progress Bar"` |

### Alpine: `rzProgress` — `setProgress(value)`, `increment(val)`, `decrement(val)`

---

## Radio Group (`RzRadioGroup`)
**File:** `radiogroup.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `For` | `Expression<Func<TValue>>` | Required |
| `Value` | `TValue?` | `default` |
| `DisplayName` | `string?` | `null` |
| `ShowIndicators` | `bool` | `true` |

### RadioGroupItem: `Value`, `Disabled`, `Id`, `ChildContent`
### RadioGroupItemIndicator: `ChildContent?`

---

## Scroll Area (`RzScrollArea`)
**File:** `scroll-area.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Orientation` | `Orientation` | `Vertical` |
| `Type` | `ScrollAreaType` | `Hover` |
| `ScrollHideDelay` | `int` | `600` |
| `ShowDefaultScrollBar` | `bool` | `true` |
| `ShowBothScrollbars` | `bool` | `false` |

### Alpine: `rzScrollArea` — `update()`

---

## Separator (`RzSeparator`)
**File:** `separator.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Style` | `SeparatorStyle` (Solid, Dashed, Dotted) | `Solid` |
| `Orientation` | `Orientation` | `Horizontal` |
| `LabelAlignment` | `Align` (Start, Center, End) | `Center` |
| `ChildContent` | `RenderFragment?` | `null` |

---

## Sheet (`RzSheet`)
**File:** `sheet.html`

### RzSheet Parameters
| Property | Type | Default |
|---|---|---|
| `DefaultOpen` | `bool` | `false` |
| `Modal` | `bool` | `true` |
| `DismissOnOutsideClick` | `bool` | `true` |
| `AriaLabel` | `string?` | `null` |

### SheetContent: `Side` (Top, Right, Bottom, Left), `ChildContent`

### Alpine: `rzSheet` — `toggle()`, `show()`, `close()`

---

## Shine Border (`RzShineBorder`)
**File:** `shine-border.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `BorderWidth` | `double` | `1` |
| `Duration` | `double` | `14` |
| `ShineColor` | `string?` | `null` |
| `ShineColors` | `IEnumerable<string>?` | `null` |
| `RespectReducedMotion` | `bool` | `true` |

CSS-driven, no events.

---

## Slider (`RzSlider`)
**File:** `slider.html`

### Parameters
| Property | Type | Description |
|---|---|---|
| `Value` | `double[]?` | Current values |
| `DefaultValue` | `double[]?` | Fallback initial |
| `Min` | `double` | Min value |
| `Max` | `double` | Max value |
| `Step` | `double` | Snap increment |
| `Orientation` | `SliderOrientation` | Horizontal/Vertical |
| `Disabled` | `bool` | Non-interactive |
| `Name` | `string?` | Form input name |
| `UseIndexedNames` | `bool` | Name[0], Name[1] |

### Alpine: `rzSlider` — Internal only

---

## Sidebar (`RzSidebarProvider`)
**File:** `sidebar.html`

### RzSidebarProvider Parameters
| Property | Type | Default |
|---|---|---|
| `DefaultOpen` | `bool` | `true` |
| `Collapsible` | `SidebarCollapsible` | `OffCanvas` (OffCanvas, Icon, None) |
| `KeyboardShortcut` | `string` | `"b"` |
| `Width` | `string` | `"16rem"` |

### Sidebar: `Side` (Left, Right), `Variant` (Sidebar, Floating, Inset), `AriaLabel`

### Alpine: `rzSidebar`
- `toggle()`, `setOpen(bool)`, `setOpenMobile(bool)`, `close()`, `isMobileOpen()`, `destroy()`
- Events: `rz:sidebar:state-change`, `rz:sidebar:breakpoint-change`, `rz:sidebar:mobile-open/close`

---

## Skeleton (`RzSkeleton`)
**File:** `skeleton.html`

### Parameters: `ChildContent` (RenderFragment, null)

Simple placeholder, CSS-driven.

---

## Spinner (`RzSpinner`)
**File:** `spinner.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Size` | `Size` | `Medium` |
| `Color` | `SemanticColor` | `None` (uses Foreground) |
| `AriaLabel` | `string?` | `null` (localized "Loading...") |

---

## Steps (`Steps`)
**File:** `steps.html`

### Steps Parameters
| Property | Type | Default |
|---|---|---|
| `Orientation` | `Orientation` | `Horizontal` |
| `ShowLabels` | `bool` | `true` |
| `AriaLabel` | `string?` | `null` |
| `ActiveColor` | `StatusColor` | `Primary` |

### Step Parameters: `Label` (required), `Status` (required), `Caption`, `Icon`, `AriaLabel`

---

## Switch (`RzSwitch`)
**File:** `switch.html`

### Parameters: `For` (Expression<Func<bool>>), `Value` (bool?), `DisplayName`, `AriaLabel` ("Toggle switch")

---

## Toggle (`RzToggle`)
**File:** `toggle.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Variant` | `ToggleVariant` | `Default` (Default, Outline) |
| `Size` | `ToggleSize` | `Default` |
| `Pressed` | `bool?` | `null` |
| `DefaultPressed` | `bool` | `false` |
| `Disabled` | `bool` | `false` |
| `ChildContent` | `RenderFragment?` | `null` |

### Alpine: `rzToggle` — `toggle()`, `state()`

---

## Tabs (`RzTabs`)
**File:** `tabs.html`

### Parameters: `DefaultValue` (string?), `ChildContent` (required)
### TabsList: `AriaLabel`, `Orientation`
### TabsTrigger: `Value` (required), `Disabled`, `AsChild`
### TabsContent: `Value` (required)

### Alpine: `rzTabs` — `refreshTriggers()`, `activateTrigger(trigger, focusTrigger)`, `onListKeydown(event)`

---

## Table (`RzTable`)
**File:** `table.html`

### RzTable: `Border` (false), `FixedHeader` (false)
### TableRow: `Selected`, `HoverableOverride`
### TableCell: `Colspan`
### Sub-components: `TableCaption`, `TableHeader`, `TableBody`, `TableFooter`, `TableHeaderCell`, `TablePagination`

---

## DataTable (`RzDataTable<TItem>`)
**File:** `datatable.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `Items` | `IReadOnlyList<TItem>` | Empty |
| `Config` | `RzDataTableConfig<TItem>` | Required |
| `RowIdSelector` | `Expression<Func<TItem, object?>>` | Required |
| `ChildContent` | `RenderFragment?` | `null` |
| `LoadStrategy` | `string` | `"eager"` |

### Alpine: `rzDataTable`
- Rendering: `headerGroups`, `rows`, `footerGroups`, `hasRows`, `isEmpty`, `selectedRowCount`
- Sorting API: `sort.can(header)`, `sort.direction(header)`, `sort.toggle(header)`, `sort.ariaSort(header)`, `sort.nextLabel(header)`
- Selection API: `selection.canSelect(row)`, `selection.isSelected(row)`, `selection.setRowSelected(row, value)`, `selection.allPageRowsSelected()`, `selection.setAllPageRows(value)`, `selection.allRowsSelected()`, `selection.setAllRows(value)`
- Pagination API: `pagination.pageIndex`, `pagination.pageSize`, `pagination.pageCount`, `pagination.canPreviousPage`, `pagination.canNextPage`, `pagination.totalRows`, `pagination.startRow`, `pagination.endRow`, `pagination.items`. Actions: `previousPage()`, `nextPage()`, `firstPage()`, `lastPage()`, `setPageIndex(index)`, `setPageSize(size)`
- Filtering API: `filter.globalFilter`, `filter.setGlobalFilter(value)`
- Columns API: `columns.all`, `columns.leaf`, `columns.visibleLeaf`, `columns.toggleAllColumnsVisible(value)`, `columns.getIsAllColumnsVisible()`, `columns.getIsSomeColumnsVisible()`
- Helper components: `DataTableSortToggle`, `DataTableSelectAllCheckbox`, `DataTableRowSelectCheckbox`, `DataTableSelectionHeaderCell`, `DataTableSelectionCell`
- x-flexrender: `_flex.header(header)`, `_flex.cell(cell)`, `_flex.footer(footer)`

---

## Animated Gradient Text (`RzAnimatedGradientText`)
**File:** `animated-gradient-text.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment?` | `null` |
| `Speed` | `decimal` | `1` |
| `ColorFrom` | `string` | `#ffaa40` |
| `ColorTo` | `string` | `#9c40ff` |
| `Animate` | `bool` | `true` |
| `Element` | `string?` | `span` |

CSS-only, no Alpine API.

---

## Toast Provider (`RzToastProvider`)
**File:** `toast-provider.html`

### Parameters (Provider)
| Property | Type | Default |
|---|---|---|
| `Position` | `ToastPosition` | `TopRight` |
| `DefaultStatus` | `ToastStatus` | `Info` |
| `Tone` | `ToastTone` | `Subtle` |
| `MaxVisible` | `int` | `5` |
| `Duration` | `int` | `4000` |
| `OverflowStrategy` | `ToastOverflowStrategy` | `DismissOldest` |
| `NewestOnTop` | `bool` | `true` |
| `Dismissible` | `bool` | `true` |
| `ShowProgress` | `bool` | `true` |
| `PauseOnHover` | `bool` | `true` |
| `CloseOnEscape` | `bool` | `true` |
| `PreventDuplicates` | `bool` | `false` |

### C# API: `IRzToastService` — `Show()`, `Success()`, `Error()`, `Warning()`, `Info()`, `Loading()`, `Update()`, `Dismiss()`, `Clear()`

### JS API: `Rizzy.toast` — `show()`, `success()`, `error()`, `warning()`, `info()`, `loading()`, `update()`, `dismiss()`, `clear()`, `custom()`

### Events: `rz:toast`, `rz:toast:update`, `rz:toast:dismiss`, `rz:toast:clear`, `rz:toast:batch`

---

## Tooltip (`RzTooltip`)
**File:** `tooltip.html`

### RzTooltip Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment` | Required |
| `Anchor` | `AnchorPoint` | `Top` |
| `DelayDuration` | `int` | `700` |
| `Open` | `bool?` | `null` |
| `DefaultOpen` | `bool` | `false` |
| `DisableHoverableContent` | `bool` | `false` |
| `AriaLabel` | `string?` | Localized |

### TooltipProvider: `DelayDuration`, `SkipDelayDuration`, `DisableHoverableContent`

### Alpine: `rzTooltip` — `queueOpen()`, `queueClose()`, `handleWindowEscape()`, `updatePosition()`

---

## Typing Animation (`RzTypingAnimation`)
**File:** `typing-animation.html`

### Parameters
| Property | Type | Default |
|---|---|---|
| `ChildContent` | `RenderFragment?` | `null` |
| `Words` | `IReadOnlyList<string>?` | `null` |
| `Duration` | `int` | `100` |
| `TypeSpeed` | `int?` | `null` |
| `DeleteSpeed` | `int?` | `null` |
| `Delay` | `int` | `0` |
| `PauseDelay` | `int` | `1000` |
| `Loop` | `bool` | `false` |
| `StartOnView` | `bool` | `true` |
| `ShowCursor` | `bool` | `true` |
| `BlinkCursor` | `bool` | `true` |
| `CursorStyle` | `TypingAnimationCursorStyle` | `Line` |
| `Element` | `string?` | `"span"` |
| `Id` | `string?` | Auto-generated |

### Alpine: `rzTypingAnimation` — Internal only

---

## Global Enums & Types

### ThemeVariant
`Default`, `Secondary`, `Primary`, `Accent`, `Information`, `Destructive`, `Warning`, `Success`, `Ghost`, `Inverse`

### Size
`ExtraSmall`, `Small`, `Medium`, `Large`, `ExtraLarge`

### StatusColor
`Primary`, `Secondary`, `Success`, `Info`, `Warning`, `Destructive`

### AnchorPoint
`TopStart`, `Top`, `TopEnd`, `LeftStart`, `Left`, `LeftEnd`, `RightStart`, `Right`, `RightEnd`, `BottomStart`, `Bottom`, `BottomEnd`

### Orientation
`Horizontal`, `Vertical`

### SemanticColor
`None`, `Primary`, `Secondary`, `Success`, `Info`, `Warning`, `Destructive`
