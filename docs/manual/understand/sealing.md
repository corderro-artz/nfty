# What sealing does and does not do

A **sealed export** is a `.tin`: your whole collection encrypted under a passphrase and marked view
only. It exists for one situation -- you want an opinion on a collection, not a copy of it.

It is worth being exact about what that buys, because the obvious reading is wrong in a way that
matters.

## What is real

**Without the passphrase, the file is noise.** The payload is encrypted with AES-256-GCM under a key
stretched from your passphrase by PBKDF2-SHA256 at 600,000 iterations. There is no recovery, no
master key, and nothing in nfty that opens a `.tin` without the passphrase it was sealed with. That
part is arithmetic and it holds.

**The view-only mark cannot be edited out.** It is authenticated with a second key derived from the
same passphrase, so somebody who intercepts the file cannot flip it and pass the result on as an
ordinary Set. Change one byte of the header or one byte of the payload and the file stops opening at
all.

**The label is honest.** A recipient who has not found the passphrase yet still sees the collection's
name, its size and your note. That is deliberate: a file that says nothing until it fails to parse
teaches its holder that something is broken rather than that something is sealed.

## What is not real

**It does not stop the person you gave the passphrase to.** nfty refuses to save or export the
assets, and it refuses in the engine rather than only in the buttons -- but the format is documented
and the source is public, so somebody determined re-implements the reader in an afternoon. **The seal
is a lock on a door, not a wall**, and the person with the key is already standing inside.

**It does not stop a screenshot.** Nothing does.

**It does not hide that the collection exists.** The name, the asset count and the note sit outside
the encryption on purpose -- see above. The art, the traits, the rarity and the DNA are all inside
it.

## So what is it for

Seal for **people you chose**, not against theft. It is the difference between "please don't pass
this around" written in an email and written into the file, plus real protection against everybody
who was never sent the passphrase in the first place -- the copy that ends up on a shared drive, the
attachment forwarded one hop too far, the backup nobody meant to keep.

If your requirement is that a viewer *cannot* obtain the art, no file you hand them can meet it. What
can is not sending the art: publish downsized or watermarked previews and keep the collection.

## Practical rules

**Send the passphrase by a different route than the file.** A `.tin` and its passphrase in one email
thread is one compromised mailbox away from being neither sealed nor private.

**Keep the original.** Sealing copies your Set, it never consumes it. There is deliberately no
`unseal` command -- one would make the whole thing theater, and you already have the Set it was
sealed from.

**A passphrase must be at least 12 characters,** and nfty refuses shorter ones rather than warning
about them. Six hundred thousand iterations is irrelevant against a passphrase that fits in a
wordlist, and the resulting file would look exactly as sealed as a real one.

!!! note "A wrong passphrase and an altered file fail identically"

    They have to. Every check runs on a key derived from what you typed, so a wrong passphrase
    produces a wrong key -- which is indistinguishable from a right key against edited bytes. nfty
    says both things it could be rather than guessing at one.
