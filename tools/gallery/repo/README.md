# nfty community gallery

The index behind nfty's **Community** page: one small JSON file per shared collection, checked by a GitHub
Action before it is listed. The pictures and Sets themselves live in each sharer's own `nfty-public`
repository; nothing big is stored here.

```
entries/<github-user-id>/<slug>.json   one per share; the only files a pull request may touch
blocked.json                           user ids and Set sha256s that are never listed
tags.json                              the tags the app offers
scripts/                               the checker and index builder (stdlib Python)
health.json                            the nightly miss counters
```

## Sharing

You don't edit this repository by hand. In nfty, open a Set, choose **Share…**, and the app uploads your
files to your own `nfty-public` repository and opens the pull request for you. A check runs on it; if it
passes it merges on its own, and if not the pull request is closed with a comment saying why.

The check looks at your account (at least 30 days old, at most 3 shares, one new share a day), your
`nfty-public` repository (nothing in it but what nfty puts there), the Set file (at most 200 MB, a safe
archive, exactly what the entry says it is), and the words you wrote (no links).

## Reporting

Use **Report a share** under Issues, or the Report button on a share's page in nfty. A takedown reverts the
entry and adds the owner or the Set's hash to `blocked.json`; the next publish removes it from every
client's next refresh.

## The index

`index.json` (and `tags.json`) are generated and published to GitHub Pages on every merge; they are never
committed. To rebuild by hand: `python scripts/build_index.py entries --blocked blocked.json > site/index.json`.
