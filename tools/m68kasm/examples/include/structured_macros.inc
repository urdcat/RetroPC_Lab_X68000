defmacro SKIP_ON_NE(BODY) = {
  bne.s macro_end
  BODY
macro_end:
}

struct base:a0 vector { x.l, y.l, z.l }
