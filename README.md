# Market POS

A till and back office for a Moroccan grocery. One computer runs the shop; a second till can be
added later without changing anything about how the first one works.

Written for one shop rather than for every shop, which is why it does a few things properly
instead of many things partly: it never deletes a financial record, it freezes what a product
cost at the moment it sold, and it keeps selling when the network is not there.

---

## What it does

**At the counter**

- Scan a barcode and the item is on the sale with its name and price. Scan it again and there
  are two of them. The quantity is the cashier's to change.
- **Price check** — scan anything to see what it is and what it costs, without touching the
  sale. The shelf price for anyone; what the shop paid for it only for the owner.
- Products with a printed barcode are scanned and nothing else. Bread, produce and anything
  without one get a picture the cashier presses.
- Hold a ticket, take it back up, apply a remise, reprint a receipt.

**Behind the counter**

- What the shop took today, what it kept, and what is running out.
- Stock, with a reason recorded for every movement.
- Suppliers: what they brought, what it cost, what is still owed.
- Bills, wages, and a profit statement you can read top to bottom and argue with line by line.
- Every change recorded against whoever made it.

**In three languages** — English, French, Arabic. Arabic lays the whole interface out right to
left. Changed in Settings; the app restarts into it.

---

## Running it

Download the latest release, unzip, run `MarketPos.exe`. Nothing to install: .NET is inside the
file, and the shop's database is created on first start in `%AppData%\MarketPos`.

Two or more computers: see [SETUP.md](SETUP.md).

## Building it

```bash
dotnet build MarketPos.sln
dotnet run --project MarketPos.csproj
```

A single-file build for a shop machine:

```bash
dotnet publish MarketPos.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/MarketPOS
```

---

## How it is put together

| | |
|---|---|
| `src/` | everything the shop knows, with no screen attached — models, repositories, money |
| `server/` | the back-office machine's server, so a second till can hand its sales over |
| `Views/`, `ViewModels/` | the till and the back office, WPF |
| `Theme/` | one palette, one type scale, one set of icons |

Split three ways so the same rule about money runs in all three places. A second copy of "what
did this cost" would be a second answer.

**SQLite, one file.** Money is stored as text, never as a float — a rounding error in a till is
money that does not exist. Dates are round-trip ISO strings.

**Nothing is deleted.** A cancelled sale is marked cancelled, a former worker is marked
inactive, a replaced photo is replaced. Every figure the shop has ever been shown can be
arrived at again.

**Permissions live in the repositories, not in the buttons.** Hiding a button is a courtesy;
`Session.Require` before a write is the control.

---

## Checking it

All of these need `MARKETPOS_DB` pointed at a throwaway file — they write real rows, and refuse
to run without it.

```bash
MarketPos.exe --selftest                         # 96 checks: layout, permissions, contrast, language
MarketPos.exe --flowtest                         # the money: buy, sell, refund, pay, write off
MarketPos.exe --linktest http://localhost:5000   # a till and a server, over a real socket
MarketPos.exe --icons out.png                    # photographs every screen, in every language
```

`--flowtest` is the one that matters. It walks a whole shop-day and asserts revenue, cost of
goods, gross profit, net profit, supplier debt and stock all come out where they should — the
failures it guards are the ones where nothing on screen looks wrong.

---

## Credits

[Inter](https://rsms.me/inter/) and [Nunito](https://fonts.google.com/specimen/Nunito) under the
SIL Open Font License. Icons from [Phosphor](https://phosphoricons.com), MIT. Licences ship in
`Assets/`.
