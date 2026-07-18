# RizzyUI Component Reference

> Library: **RizzyUI** v0.9.2-alpha — NuGet: `RizzyUI`  
> Source: [github.com/JalexSocial/RizzyUI](https://github.com/JalexSocial/RizzyUI)  
> Docs: [rizzyui.jalex.io](https://rizzyui.jalex.io)  
> Framework: Blazor Static SSR + Alpine.js + Tailwind CSS v4

---

## Table of Contents

1. [Link (RzLink)](#1-link-rzlink)
2. [AuroraText (RzAuroraText)](#2-auroratext-rzauratext)
3. [Highlighter (RzHighlighter)](#3-highlighter-rzhighlighter)
4. [NavigationMenu (RzNavigationMenu)](#4-navigationmenu-rznavigationmenu)
5. [Marquee (RzMarquee)](#5-marquee-rmarquee)
6. [Collapsible (RzCollapsible)](#6-collapsible-rzcollapsible)
7. [Card (RzCard & family)](#7-card-rzcard--family)
8. [ShineBorder (RzShineBorder)](#8-shineborder-rzshineborder)
9. [Table (RzTable & family)](#9-table-rztable--family)
10. [AnimatedGradientText (RzAnimatedGradientText)](#10-animatedgradienttext-rzanimatedgradienttext)

---

## 1. Link (RzLink)

**Tag:** `<RzLink>`  
**Sub-components:** None (single component)

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Href` | `string?` | `"#"` | URL the link navigates to |
| `Color` | `SemanticColor` | `Primary` | Color variant |
| `Underline` | `bool` | `true` | Shows underline on hover/focus |
| `ChildContent` | `RenderFragment?` | `null` | Inner content |
| `Element` | `string` (inherited) | `"a"` | Override rendered tag |
| `Id` | `string?` (inherited) | auto `"rz-…"` | DOM id |
| `AdditionalAttributes` | dictionary (inherited) | `null` | Passes `class`, `target`, `rel`, etc. |

**SemanticColor values:** `Primary`, `Secondary`, `Success`, `Warning`, `Destructive`, `Info`, `Foreground`, `None`

### Base CSS
```
font-medium underline-offset-2 focus:outline-hidden
```

### Usage
```razor
<RzLink Href="/page">Default link</RzLink>
<RzLink Href="/page" Color="SemanticColor.Destructive">Delete</RzLink>
<RzLink Href="https://example.com" target="_blank" rel="noopener">External</RzLink>
<RzLink Href="#" Underline="false">No underline</RzLink>
```

---

## 2. AuroraText (RzAuroraText)

**Tag:** `<RzAuroraText>`  
**Type:** CSS-only inline effect, no Alpine.js required

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | `null` | Text to animate |
| `Colors` | `string[]?` | `["#FF0080", "#7928CA", "#0070F3", "#38bdf8"]` | Gradient color stops |
| `Speed` | `double` | `1` | Animation speed multiplier (≤0 → 0.1) |
| `AriaLabel` | `string?` | `null` | Accessible name |
| `Decorative` | `bool` | `false` | Hides from assistive tech |

### Usage
```razor
<RzAuroraText>beautiful</RzAuroraText>
<RzAuroraText Colors='new[] { "#F97316", "#FACC15", "#10B981", "#06B6D4" }' Speed="2">energy</RzAuroraText>
```

---

## 3. Highlighter (RzHighlighter)

**Tag:** `<RzHighlighter>`  
**Type:** Alpine.js-driven animated inline highlight/underline

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | `null` | Text to highlight |
| `Action` | `HighlighterAction` | `Highlight` | `Highlight` (marker) or `Underline` (stroke) |
| `Color` | `string?` | `var(--highlight-1)` | CSS color |
| `StrokeWidth` | `double` | `1.5` | Stroke thickness |
| `AnimationDuration` | `int` | `600` | Duration in ms |
| `Iterations` | `int` | `2` | Drawing iterations |
| `Padding` | `int` | `2` | Padding around text |
| `Multiline` | `bool` | `true` | Support wrapped prose |
| `StartOnView` | `bool` | `false` | Animate when visible |
| `ViewMargin` | `string?` | `"-10%"` | IntersectionObserver margin |

### Usage
```razor
<RzHighlighter>small visual emphasis</RzHighlighter>
<RzHighlighter Action="HighlighterAction.Underline" Color="#ff9800">animated underline</RzHighlighter>
<RzHighlighter Color="var(--highlight-3)" AnimationDuration="500">Custom timing</RzHighlighter>
<RzHighlighter StartOnView="true">Animates on scroll</RzHighlighter>
```

---

## 4. NavigationMenu (RzNavigationMenu)

**Tag:** `<RzNavigationMenu>`  
**Sub-components:** `NavigationMenuList`, `NavigationMenuItem`, `NavigationMenuTrigger`, `NavigationMenuContent`, `NavigationMenuLink`

Composite navigation bar with accessible dropdown panels.

### Root Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | `null` | Menu content |
| `Orientation` | `Orientation` | `Horizontal` | Menu orientation |
| `AriaLabel` | `string?` | `"Main Navigation"` | Accessible name |

### Structure
```razor
<RzNavigationMenu>
    <NavigationMenuList>
        <NavigationMenuItem>
            <NavigationMenuTrigger>Label</NavigationMenuTrigger>
            <NavigationMenuContent>
                @* Dropdown content *@
            </NavigationMenuContent>
        </NavigationMenuItem>
        <NavigationMenuItem>
            <NavigationMenuLink AsChild Href="/page">
                <RzLink Color="SemanticColor.Foreground" Underline="false">Direct Link</RzLink>
            </NavigationMenuLink>
        </NavigationMenuItem>
    </NavigationMenuList>
</RzNavigationMenu>
```

### Keyboard Navigation
| Key | Behavior |
|-----|----------|
| Enter/Space | Toggles trigger panel |
| ArrowLeft/Right | Move between top-level items |
| ArrowDown | Open panel and focus first descendant |
| Escape | Close panel, restore focus to trigger |
| Tab | Normal tab order (no trap) |

---

## 5. Marquee (RzMarquee)

**Tag:** `<RzMarquee>`  
**Type:** CSS-only infinite scroll, no Alpine.js required

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Orientation` | `MarqueeOrientation` | `Horizontal` | Direction |
| `Reverse` | `bool` | `false` | Reverse direction |
| `PauseOnHover` | `bool` | `false` | Pause on hover/focus |
| `Repeat` | `int` | `4` | Rendered segments (clamped ≥2) |
| `Duration` | `string` | `"40s"` | CSS animation duration |
| `Gap` | `string` | `"1rem"` | Gap between items |
| `ChildContent` | `RenderFragment?` | `null` | Content to scroll |

### Usage
```razor
<RzMarquee PauseOnHover Duration="20s">
    <div class="w-64 shrink-0">Item 1</div>
    <div class="w-64 shrink-0">Item 2</div>
</RzMarquee>

<RzMarquee Reverse Orientation="MarqueeOrientation.Vertical" PauseOnHover Duration="20s">
    @* vertical items *@
</RzMarquee>
```

---

## 6. Collapsible (RzCollapsible)

**Tag:** `<RzCollapsible>`  
**Sub-components:** `CollapsibleTrigger`, `CollapsibleContent`

### RzCollapsible Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment` | **Required** | Trigger + Content |
| `DefaultOpen` | `bool` | `false` | Start expanded |

### CollapsibleTrigger Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment` | **Required** | Button content |
| `AsChild` | `bool` | `false` | Merge into first child element |

### CollapsibleContent Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | `null` | Panel content |

### Usage
```razor
<RzCollapsible DefaultOpen="true">
    <CollapsibleTrigger AsChild>
        <RzButton Variant="ThemeVariant.Ghost">Toggle</RzButton>
    </CollapsibleTrigger>
    <CollapsibleContent>
        <p>Hidden content here</p>
    </CollapsibleContent>
</RzCollapsible>
```

---

## 7. Card (RzCard & family)

**Tag:** `<RzCard>`  
**Sub-components:** `CardHeader`, `CardTitle`, `CardDescription`, `CardAction`, `CardContent`, `CardFooter`

### RzCard Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | `null` | Card content |
| `Element` | `string` | `"div"` | Root HTML element |
| *(inherited)* | `Id`, `LoadStrategy`, `AdditionalAttributes` | — | — |

**Default CSS:** `flex flex-col gap-6 rounded-xl border py-6 shadow-sm bg-card text-card-foreground`

### Sub-component defaults
| Component | Element | Classes |
|-----------|---------|---------|
| `CardHeader` | `<header>` | `@container/card-header grid auto-rows-min grid-rows-[auto_auto] items-start gap-2 px-6 has-data-[slot=card-action]:grid-cols-[1fr_auto] [.border-b]:pb-6` |
| `CardTitle` | `<h2>` | `leading-none font-semibold` |
| `CardDescription` | `<p>` | `text-muted-foreground text-sm` |
| `CardAction` | `<div>` | `col-start-2 row-span-2 row-start-1 self-start justify-self-end` |
| `CardContent` | `<section>` | `px-6` |
| `CardFooter` | `<footer>` | `flex items-center px-6 [.border-t]:pt-6` |

### Usage
```razor
<RzCard class="max-w-sm">
    <CardHeader>
        <CardTitle>Title</CardTitle>
        <CardDescription>Subtitle text</CardDescription>
        <CardAction>
            <RzButton Size="Size.Small">Action</RzButton>
        </CardAction>
    </CardHeader>
    <CardContent>Body content</CardContent>
    <CardFooter>Footer content</CardFooter>
</RzCard>
```

---

## 8. ShineBorder (RzShineBorder)

**Tag:** `<RzShineBorder>`  
**Type:** Decorative animated border overlay (CSS + Alpine.js)

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `BorderWidth` | `double` | `1` | Border thickness in px |
| `Duration` | `double` | `14` | Animation loop in seconds |
| `ShineColor` | `string?` | `null` | Single CSS color |
| `ShineColors` | `IEnumerable<string>?` | `null` | Multi-stop gradient colors |
| `RespectReducedMotion` | `bool` | `true` | Uses `motion-safe:` prefix |

### Usage
```razor
<div class="relative overflow-hidden rounded-xl border bg-card p-6">
    <RzShineBorder BorderWidth="2" Duration="8" ShineColor="var(--primary)" />
    <RzHeading Level="HeadingLevel.H3">Content</RzHeading>
</div>

<RzShineBorder ShineColors="@(new[] { "#A07CFE", "#FE8FB5", "#FFBE7B" })" />
```

**Must use:** Host container must have `relative` + `overflow-hidden`. RzShineBorder is a **sibling**, not a wrapper.

---

## 9. Table (RzTable & family)

**Tag:** `<RzTable>`  
**Sub-components:** `TableCaption`, `TableHeader`, `TableBody`, `TableFooter`, `TableRow`, `TableHeaderCell`, `TableCell`, `TablePagination`

### RzTable Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | **Required** | Table content |
| `Border` | `bool` | `false` | Adds rounded border |
| `FixedHeader` | `bool` | `false` | Sticky header |
| `Id` | `string?` | — | HTML id |

### TableRow Parameters
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Selected` | `bool` | `false` | Selected styling |
| `HoverableOverride` | `bool?` | `null` | Override hover |

### TableCell Parameters
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Colspan` | `int` | `1` | Column span |

### TableHeaderCell
- Automatically sets `scope="col"`

### Usage
```razor
<RzTable Border>
    <TableCaption>Recent invoices</TableCaption>
    <TableHeader>
        <TableRow>
            <TableHeaderCell>Invoice</TableHeaderCell>
            <TableHeaderCell class="text-right">Amount</TableHeaderCell>
        </TableRow>
    </TableHeader>
    <TableBody>
        <TableRow>
            <TableCell class="font-medium">INV001</TableCell>
            <TableCell class="text-right">$250.00</TableCell>
        </TableRow>
    </TableBody>
</RzTable>
```

---

## 10. AnimatedGradientText (RzAnimatedGradientText)

**Tag:** `<RzAnimatedGradientText>`  
**Type:** CSS-only animated gradient text, no Alpine.js

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | `null` | Text content |
| `Speed` | `decimal` | `1` | Animation speed (≤0 → 0.1) |
| `ColorFrom` | `string` | `#ffaa40` | Gradient start color |
| `ColorTo` | `string` | `#9c40ff` | Gradient end color |
| `Animate` | `bool` | `true` | Enable/disable animation |
| `Element` | `string?` | `"span"` | Root HTML element |

### Usage
```razor
<RzAnimatedGradientText class="text-2xl font-bold">
    Gradiented heading
</RzAnimatedGradientText>

<RzAnimatedGradientText Speed="2" ColorFrom="#4ade80" ColorTo="#06b6d4"
                         Element="h1" class="text-3xl font-bold">
    Custom gradient
</RzAnimatedGradientText>
```

---

## 17. Tabs (RzTabs & Family)

A keyboard-navigable tabs component with SSR-safe ARIA roles, IDs, and relationships. Uses Alpine.js for client-side activation and roving tabindex.

**Namespace:** `RizzyUI` (auto-imported via `@using RizzyUI`)

**Structure:** `RzTabs` → `TabsList` → `TabsTrigger` × N + `TabsContent` × N

### RzTabs

Root container for the tabs component.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `DefaultValue` | `string` | `null` | Default active tab value |
| `Value` | `string` | `null` | Controlled value (two-way binding) |
| `OnValueChange` | `EventCallback<string>` | — | Callback when value changes |

### TabsList

The container for trigger buttons.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `AriaLabel` | `string` | `null` | ARIA label for the tablist |
| `Orientation` | `Orientation` | `Horizontal` | Tablist orientation |

Renders: `div[role=tablist]`

### TabsTrigger

Individual tab button.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Value` | `string` | **req'd** | The value this trigger represents |
| `Disabled` | `bool` | `false` | Disable the trigger |
| `AsChild` | `bool` | `false` | Render as child element |

Renders: `button[role=tab]` with `data-state="active|inactive"` and `aria-selected`

**Styling:** Use `data-[state=active]:*` and `data-[state=inactive]:*` to style active vs inactive states.

### TabsContent

Content panel associated with a tab value.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Value` | `string` | **req'd** | Matches a trigger's value |

Renders: `div[role=tabpanel]` with `aria-labelledby` referencing the active trigger.

### Alpine.js API

The component registers an `rzTabs` Alpine component:

| Method | Description |
|--------|-------------|
| `activateTrigger(value, options?)` | Selects a trigger, dispatches `rz:tabs-change` |

**Event:** `rz:tabs-change` — dispatched when the value changes. Access via `$event.detail.value`.

### Example

```razor
<RzTabs DefaultValue="account" class="w-full">
    <TabsList class="flex gap-2 border-b border-border">
        <TabsTrigger Value="account"
                     class="px-4 py-2 data-[state=active]:border-b-2 data-[state=active]:border-primary">
            Account
        </TabsTrigger>
        <TabsTrigger Value="password"
                     class="px-4 py-2 data-[state=active]:border-b-2 data-[state=active]:border-primary">
            Password
        </TabsTrigger>
    </TabsList>
    <TabsContent Value="account">
        <p>Account settings here.</p>
    </TabsContent>
    <TabsContent Value="password">
        <p>Password settings here.</p>
    </TabsContent>
</RzTabs>
```

---

## Quick Reference: Replacement Map

| Native HTML | RizzyUI Replacement |
|-------------|-------------------|
| `<a href="...">` | `<RzLink Href="...">` |
| `<h1>..<h6>` | `<RzHeading Level="...">` |
| `<p>` | `<RzParagraph>` |
| `<div class="card">` | `<RzCard>` + sub-components |
| `<table>` | `<RzTable>` + family |
| `<mark>` / `<u>` | `<RzHighlighter>` |
| `<span class="gradient-text">` | `<RzAnimatedGradientText>` |
| `<nav>` | `<RzNavigationMenu>` |
| `<marquee>` / scroll div | `<RzMarquee>` |
| `<details>` / `<summary>` | `<RzCollapsible>` + family |
| `div.shine-border` | `<RzShineBorder>` |
| `button` | `<RzButton>` |
| `input[type=file]` | `<RzFileInput>` |
| `img.avatar` | `<RzAvatar>` |
| `code`, `kbd` | `<RzKbd>` |

> **Tip:** Pass `class` via `AdditionalAttributes` on any RizzyUI component to add Tailwind classes (merged automatically via TwMerge).
