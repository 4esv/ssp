# Models

This folder contains device models. Each model is written from datasheet values (clean-room rule).

## Provenance header

Each model file starts with these comment lines:

```spice
* provenance: <datasheet title>, <manufacturer>, <datasheet URL>
* fit: <the datasheet points that the parameters match>
* author: <name>
* license: MIT
```

A model file without this header fails review.
