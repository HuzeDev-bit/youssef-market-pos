# Market POS — running it in the shop

## One computer (the normal case)

Nothing to set up. Run `MarketPos.exe`. The till and the back office are the same program, and
the database lives at `%AppData%\MarketPos\marketpos.db`.

Leave **Settings → Back office address** empty. The shop never touches the network.

---

## Two or more computers

One machine owns the books. Everything else is a till that keeps its own copy of the
catalogue, sells with or without the network, and hands its sales over afterwards.

### On the back-office machine

Start the server. It reads and writes the same `marketpos.db` the back office uses.

```bash
dotnet run --project server
```

It listens on port 5000 on every network the machine is on, and prints the addresses a till
should be pointed at:

```
Tills should be pointed at http://192.168.1.20:5000
```

Two things to check the first time:

- **Windows Firewall** will ask whether to allow it. Say yes for *private* networks.
- The machine needs a **fixed address** on the shop's router, or the tills will be pointing at
  an address that moves. Any router's DHCP reservation screen will do it.

The back office itself keeps working exactly as before while the server runs — they share the
database file on the one machine, which is the only place SQLite is safe to share.

### On each till

**Settings → Back office address**: type what the server printed, e.g. `192.168.1.20:5000`
(the `http://` is added for you). Give the till a name while you are there — it goes on every
sale that came from it.

Press **Test**. It will say which shop it found, or what is wrong in words you can act on.

From then on the till shows a small chip in its top corner:

| It says | It means |
|---|---|
| **Connected** | everything is going straight to the books |
| **Connected · sending 3** | catching up, no action needed |
| **Working offline** | the back office is off. Keep selling. |
| **Offline · 3 sales waiting** | keep selling; they go over when it comes back |

Pressing the chip forces a send. Nothing is ever lost by pressing it.

### What happens when the back office is off

The till sells. Every sale is written to the till's own database and queued. When the back
office comes back — by itself, within half a minute — the queue empties into it.

A sale is only ever counted once. Each one carries a reference the till minted, and the server
recognises a repeat rather than taking the money twice. This is tested; see below.

### Signing in on a second till

The staff list travels with the catalogue, so the sign-in box on a till shows the same people
as the back office and checks their password with nothing plugged in. That means a cashier can
start their shift on a till even when the back-office machine is off.

The password itself never travels — what does is a PBKDF2-SHA256 hash and its salt, and only
for staff the owner has actually given a password to. Take someone's password away in the back
office and it stops working on every till at the next sync.

Because those hashes sit on each till, a four-digit PIN is worth less than a real password.
Give staff something longer than four digits if the tills are anywhere a stranger could get at
them.

### What does not travel to a till

Purchase prices, suppliers, minimum stock levels and photographs stay on the back-office
machine. A till needs none of them to sell, and what the shop paid for its stock is the
owner's business.

That is also why **price check** shows the shelf price to anyone at any till, but only shows
what the shop paid when the owner is signed in on the machine that holds the books.

---

## Checking it works

All of these need `MARKETPOS_DB` pointed at a throwaway file — they write real rows, and they
refuse to run without it.

```bash
MarketPos.exe --selftest                              # 82 checks: layout, permissions, contrast
MarketPos.exe --flowtest                              # money: buy, sell, refund, pay, write off
MarketPos.exe --linktest http://localhost:5000        # a till and a server, over a real socket
MarketPos.exe --icons out.png                         # photographs every screen, for looking at
```

`--linktest` is the one that matters here. It proves, against a running server: the catalogue
comes down, asking twice costs nothing, the staff list arrives and a wrong password is still
refused on the till, a sale queues, the queue empties, a repeat is not counted twice, the shop
keeps selling with the server unplugged, and the takings catch up when it returns.

`--flowtest` ends with a whole day done as a cashier rather than as the owner: sign in with the
right password, scan to check a price, sell, take the money, and be refused everything a
cashier should not have.
