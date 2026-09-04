
*[CookBook]: Your whole project - canvas size, collection details and Recipes. Saved as .cbk
*[Recipe]: One character type - an ordered stack of layers plus its rules. Saved as .rcp
*[Ingredient]: One layer - its kind, its coloring and its weighted variants. Saved as .igt
*[Variant]: One option inside a layer - a drawing, a name and a weight
*[Kitchen]: Your shelf of loose parts - Ingredients and Recipes belonging to no one CookBook. Saved as .ktn
*[DNA]: An asset's fingerprint, hashed from its recipe, variants and quantized colors
*[Dynamic]: A layer kind - a gray value-map whose color is rolled per asset
*[Static]: A layer kind - a gray value-map with one fixed color on every asset
*[Custom]: A layer kind - full-color art composited exactly as drawn
*[value-map]: A grayscale drawing where the gray means lightness, to be colored at generation time
*[quantize]: How finely a color range is divided into colors nfty treats as different
