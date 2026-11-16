# Subscription lifecycle

Promo codes (`#98`), mid-cycle proration (`#114`), vacation holds (`#229`) and a cancel flow that
asks why (`#174`). Everything that happens to a subscription between signing up and leaving.

They are one batch because they interlock: the save offer in the cancel flow *is* a pause or a
promo code, and both have to exist before the offer means anything.

---

## This is money, so the rules are stated as things that must never happen

- **A code must never be redeemable twice by one account.** The unique index on
  `(promo_code_id, user_id)` is the enforcement — the check before it is a courtesy that two
  concurrent requests both pass.
- **Quoting must never consume a code.** A hundred-use code would otherwise run out on twenty
  abandoned checkouts. Quote at checkout, redeem when money moves.
- **A discount must never make a price negative.** A fixed-amount code larger than the price makes
  the purchase free, and stops there.
- **A hold must never grant free time, and must never burn paid time.** Both failure modes are why
  people distrust a pause button.
- **Proration must never pay out.** An upgrade can be free; it cannot be a refund.

## Promo codes (`#98`)

Percent or fixed amount, optional expiry, optional redemption limit, optional plan restriction,
optional first-time-only.

Stored upper-cased and compared case-insensitively, because the one certainty about a code printed
on a conference sticker is that somebody types it in lower case.

**"No such code" and "expired code" return the same error.** Telling a stranger which of their
guesses exists turns the code list into something worth enumerating.

**First-time-only means never paid**, not "no active subscription". An acquisition code that
existing subscribers can apply is a discount on revenue you already had.

Redemption failing after payment is deliberately *not* fatal. The provider has already collected
the discounted amount; refusing to record the redemption would lose the customer the plan they
just paid for over an accounting detail.

## Proration (`#114`)

Charged for a month, three weeks used, upgrading today: the remaining week is already paid for and
comes off the new plan's price.

Without this, upgrading mid-cycle is a penalty — which is why people wait for renewal, and why
some of them never come back to it.

Applied **only upwards**. A downgrade keeps the better plan until the period ends; refunding the
difference would mean paying money back for a service already delivered, and every subscription
product resolves that the same way for the same reason.

Not applied to a trial (nothing was paid) or a paused subscription (a stopped clock has no
unused portion to credit). Capped at the new amount.

The credit is recorded on the `PaymentIntent` alongside the promo code, so a discounted payment
can be reconciled against a list price later rather than looking like an underpayment.

## Vacation hold (`#229`)

Pausing banks the remaining paid days, rounded **up** — a subscriber with three hours left gets a
day back, not zero, and a few hours of service is not worth withholding.

While paused the account is on **Free**. Their files stay; the paid limits do not, because the
point of a hold is that nothing is being charged for.

Resuming sets the period to `now + banked days`. Time spent paused costs nothing and earns
nothing: a hold is a stopped clock, not a free extension.

A paused subscriber is on the Free plan but **must still see their hold**, so the billing page
checks `IsPaused` before showing the upgrade prompt. Getting this wrong hides the resume button
from the only person who needs it — which is how the first version of this shipped, and what a
test now prevents.

## Cancel flow (`#174`)

The reason is a closed set so the answers can be counted, plus free text, because a survey nobody
can answer in their own words tells you only what you already thought to ask.

**The offer matches the reason.** Somebody taking a break does not want money off — they want to
stop paying for two months and come back. Offering them a discount is answering a question they
did not ask, and it is why so many cancel flows feel like a fight. `PreferPause` encodes that:
"temporary break" and "not using it" get a hold, everything else gets the retention code.

**Offered once.** `SaveOfferShown` is set on the first attempt. An offer that reappears every time
somebody tries to leave is an obstacle, not a retention tool, and they will remember which it was.

**The retention discount is not invented in code.** The flow looks for a live promo code named
`STAY`; if the operator has not created one, no discount is offered and the pause is still there.
A hard-coded percentage in a cancel flow is a price change nobody signed off on.

**Taking the offer is still recorded.** A save that is not counted looks exactly like a customer
who never tried to leave, and then nobody knows whether the offer works.

Cancelling keeps the plan until the period ends. Cancelling is not a refund request.

## Invariants for anyone changing this

- **Quote and redeem stay separate.** Anything that consumes a code before payment is a bug.
- **Clamp every discount to `[0, amount]`.**
- **A hold banks days; it never extends or shortens them.**
- **Proration is upward-only and capped at the new price.**
- **Any new plan-changing path must respect `IsPaused`** — a paused account is on Free, and code
  that reads `User.Plan` without checking will conclude they never subscribed.

## Not shipped

- **Admin UI for creating promo codes.** They are rows; an operator creates them directly today.
  A management screen is worth building when somebody is running campaigns, and pretending
  otherwise would put a CRUD screen in front of a table nobody is editing yet.
- **Automatic resume at a chosen date.** `MaxPauseDays` exists as a constant and nothing enforces
  it yet — a hold currently runs until it is lifted. Stated plainly rather than half-built.
- **Stacking codes.** One code per checkout. Stacking is a pricing decision, not a feature.
- **Refunds and credit notes.** Proration is a discount on the next charge, never a payout.
- **Dunning / failed-payment recovery** (`#140`) — separate issue, separate flow.
- **Multi-currency** (`#120`) — amounts are in the configured currency's smallest unit throughout.

## Tests

`tests/QuickShare.Server.Tests/E2E/SubscriptionLifecycleTests.cs` (31).

Written as the "must never happen" list above: three quotes leaving the redemption count at zero,
a second redemption by the same account failing, a fixed discount larger than the price landing at
zero rather than below it, thirty days of pause returning exactly the banked twenty, and a year of
Studio credited against a month of Pro capping at the month's price rather than paying out.
