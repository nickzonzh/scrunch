# Scrunch — Product Brief

Historical concept brief; see README for the implemented product and current scope.

**Status:** Concept / V0  
**Platform:** Windows  
**Type:** Free native desktop utility  
**Product name:** Scrunch

## One-line idea

A beautiful Windows sticky notes app that behaves as much like real sticky notes as a digital product can: visible, tactile, spatial, lightweight and intentionally simple.

## Problem

Physical sticky notes work because they are:

- immediately visible;
- spatial — where you put them matters;
- frictionless to create;
- temporary by nature;
- difficult to ignore;
- satisfying to discard when finished.

Most digital notes products lose those qualities by placing notes inside an app, database, sidebar or productivity system.

The goal is not to build a better notes database.

The goal is to reproduce the behaviour and charm of physical sticky notes on the Windows desktop.

## Product thesis

**Notes are objects, not records.**

A sticky note should feel like something the user takes, writes on, places somewhere and eventually throws away.

The Windows desktop itself is the organisational surface.

No folders, projects, tags or elaborate information architecture should be required.

## Target user

Someone who currently uses physical sticky notes for:

- quick reminders;
- small tasks;
- temporary thoughts;
- things they need to keep visible;
- short-lived information.

The initial user is the builder. Broader usefulness is secondary to making the interaction exceptionally good.

## Product principles

### Analog first

Where appropriate, interactions should borrow from physical paper rather than conventional productivity software.

### Instant

Delight must never make the app slower to use.

Animations should be fast, responsive and interruptible.

### Visible

Notes live on the desktop rather than inside a central application window.

### Spatial

Position should persist because placement itself carries meaning.

### Impermanent

Deleting a note should feel natural and satisfying. The product should encourage notes to disappear when they are no longer useful.

### Beautiful

The application should feel unusually considered for a Windows utility.

### Small

Feature restraint is part of the product.

## V0 experience

### Create

A global keyboard shortcut creates a new sticky note.

The note appears using a subtle **peel-from-pad** interaction:

1. the paper lifts;
2. its edge bends slightly;
3. the shadow changes;
4. the note settles onto the desktop.

The animation should take fractions of a second, not become theatre.

### Write

Click and type.

Text autosaves immediately.

Default typography should feel informal and handwritten while remaining highly legible.

### Place

Notes can be freely dragged anywhere on the desktop.

While dragging:

- the note may rotate slightly based on movement;
- its shadow lifts;
- movement remains directly responsive to the cursor.

When released:

- the note settles quickly;
- subtle spring or paper-like motion implies that it has been stuck down.

Its size, rotation and screen position persist between sessions.

### Resize

Notes can be resized naturally.

The design should retain the feeling of a piece of paper rather than a conventional application window.

### Colour

Users can choose from a restrained interpretation of familiar sticky-note colours:

- classic yellow;
- pale pink;
- mint;
- soft blue;
- lavender;
- peach.

Colours should feel like physical stationery rather than saturated UI tokens.

### Pin

A note can optionally remain above other windows.

This should be extremely easy to toggle per note.

### Complete / discard

Deleting a note is one of the signature interactions.

The delightful path:

1. the note crumples;
2. it becomes a small paper ball;
3. it is thrown toward a bin, screen edge or implied off-screen destination;
4. it disappears.

The motion should be quick and satisfying.

A keyboard shortcut should still allow immediate deletion without animation becoming an obstacle.

## Visual direction

The UI should feel:

- tactile;
- warm;
- handmade;
- minimal;
- slightly imperfect;
- polished rather than kitschy.

### Paper

Use extremely subtle:

- grain;
- fibres or noise;
- edge variation;
- depth and shadow.

Avoid exaggerated scrapbook textures.

### Typography

Primary note text should use a legible handwritten or hand-drawn typeface.

UI chrome, settings and accessibility surfaces can use a clean Windows-native sans serif.

Typography should feel human without compromising readability.

### Motion

Motion should communicate material:

- peel;
- flex;
- tilt;
- settle;
- crumple;
- throw.

Avoid generic fades, slides and scale animations where a physical metaphor can communicate the action more naturally.

## V0 feature set

### Required

- Native Windows desktop application
- Global shortcut to create a note
- Multiple independent sticky-note windows
- Editable plain text
- Automatic local persistence
- Persistent position and size
- Draggable notes
- Resizable notes
- Sticky-note colour selection
- Optional always-on-top
- Launch at startup
- Fast delete
- Crumple-and-throw delete interaction
- Peel/create interaction
- Paper-inspired movement and shadows
- Entirely local storage
- No account required

### Strong V0.1 candidates

- Checkbox / tiny task-list mode
- Collapse note into a small edge tab
- Slight manual or generated rotation
- Pin note across all virtual desktops
- Tiny desktop note pad used as an alternate creation affordance

## Explicitly out of scope

Do not add:

- AI;
- accounts;
- cloud sync;
- collaboration;
- folders;
- projects;
- tags;
- knowledge management;
- kanban boards;
- rich text editing;
- markdown as a primary interaction;
- productivity analytics;
- prioritisation systems;
- team features;
- reminders unless a compelling physical metaphor emerges later.

The product should resist becoming a task manager.

## Interaction quality bar

Every core interaction should satisfy two requirements:

**Utility:** faster or no slower than a conventional implementation.

**Delight:** gives the user a small sense of interacting with a physical object.

If an effect is charming but creates friction, reduce or remove it.

## Technical direction

Preferred initial exploration:

**C# + WinUI 3 / Windows App SDK**

Reasons:

- genuinely Windows-native positioning;
- strong desktop/window control;
- Windows Composition APIs for fluid motion;
- suitable foundation for custom shadows, transforms and physics-like interactions;
- useful portfolio demonstration beyond web application development.

Technical implementation should remain flexible if another Windows-native approach materially improves interaction quality.

## Data model

Keep the persistent model deliberately small.

A note needs approximately:

- ID;
- text;
- colour;
- x/y position;
- width/height;
- rotation;
- always-on-top state;
- created timestamp;
- updated timestamp.

Local persistence should be simple and inspectable.

## Success criteria

V0 succeeds if:

1. creating a note feels faster than reaching for a physical Post-it;
2. notes remain reliably where the user leaves them;
3. the app feels native and lightweight;
4. moving and discarding notes feels unusually satisfying;
5. the user naturally leaves the app running all day;
6. after several days, the app has replaced at least some physical sticky-note behaviour.

Download numbers and monetisation are not V0 success measures.

## Portfolio story

> I kept using physical sticky notes because digital note apps hide information inside applications. I wanted to see whether the useful qualities of a Post-it — visibility, spatial memory, tactility and impermanence — could survive digitisation. So I built a Windows-native utility where notes behave more like physical objects than database records.

## V0 build target

Build one exceptional loop before adding anything else:

**Create → write → place → leave visible → complete → scrunch → throw away.**

If that loop feels great, the product works.

Everything else is secondary.