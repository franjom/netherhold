# docs/wiki

The source of the GitHub wiki. CI's `wiki` job copies this folder, except this
README, to `netherhold.wiki.git` after the docs and kb gate passes on `master`.

Edit the pages here and change them through a pull request. An edit made on the
wiki's web page is overwritten by the next sync.

File names are wiki page names: `Solution-Map.md` is the page `Solution Map`,
and `_Sidebar.md` is the sidebar on every page. Links between pages use the
wiki's own syntax, `[[Solution Map|Solution-Map]]`, so they work on the wiki,
not when read here.
