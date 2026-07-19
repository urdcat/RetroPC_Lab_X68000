XDEV68K_DIR ?= $(HOME)/xdev68k

RUN68 := $(XDEV68K_DIR)/run68/run68
HAS := $(RUN68) $(XDEV68K_DIR)/x68k_bin/HAS060.X
HLK := $(RUN68) $(XDEV68K_DIR)/x68k_bin/hlk301.x
INCLUDE_DIR := $(XDEV68K_DIR)/include/xc

BUILD_DIR := build
HELLO_OBJECT := $(BUILD_DIR)/hello.o
HELLO_LIST := $(BUILD_DIR)/hello.lst
HELLO_TARGET := $(BUILD_DIR)/HELLO.X
BREAKOUT_OBJECT := $(BUILD_DIR)/breakout.o
BREAKOUT_LIST := $(BUILD_DIR)/breakout.lst
BREAKOUT_TARGET := $(BUILD_DIR)/BREAKOUT.X
TARGETS := $(HELLO_TARGET) $(BREAKOUT_TARGET)

.PHONY: all clean

all: $(TARGETS)

$(BUILD_DIR):
	mkdir -p $@

$(HELLO_OBJECT): hello.s | $(BUILD_DIR)
	$(HAS) -e -u -w0 -I$(INCLUDE_DIR) -o $@ $<

$(HELLO_LIST): $(HELLO_OBJECT)
	printf '$(HELLO_OBJECT)\n' > $@

$(HELLO_TARGET): $(HELLO_OBJECT) $(HELLO_LIST)
	$(HLK) -o $@ -i $(HELLO_LIST)
	@test -s $@ || (echo "HLK did not create $@" >&2; false)

$(BREAKOUT_OBJECT): breakout.s | $(BUILD_DIR)
	$(HAS) -e -u -w0 -I$(INCLUDE_DIR) -o $@ $<

$(BREAKOUT_LIST): $(BREAKOUT_OBJECT)
	printf '$(BREAKOUT_OBJECT)\n' > $@

$(BREAKOUT_TARGET): $(BREAKOUT_OBJECT) $(BREAKOUT_LIST)
	$(HLK) -o $@ -i $(BREAKOUT_LIST)
	@test -s $@ || (echo "HLK did not create $@" >&2; false)

clean:
	rm -rf $(BUILD_DIR)
