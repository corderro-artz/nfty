# Doing it from the command line

Everything the app does, the command line does too — useful for scripting or for checking a project
before you cook it.

```bash
nfty inspect  mybook.cbk          # what is in it
nfty inspect  mybook.cbk --voxel  # which art has partial transparency
nfty validate mybook.cbk          # is anything wrong
nfty stats    mybook.cbk          # what odds do the weights imply
nfty preview  cat.rcp --seed alpha --out preview.png
nfty generate mybook.cbk --count 500 --seed launch --out ./collection
nfty extend   mybook.cbk ./collection --to 750
```

`nfty --help`, or `nfty <command> --help`, explains any of them.
