XDEV68K_DIR ?= $(HOME)/xdev68k

RUN68 := $(XDEV68K_DIR)/run68/run68
HAS := $(RUN68) $(XDEV68K_DIR)/x68k_bin/HAS060.X
HLK := $(RUN68) $(XDEV68K_DIR)/x68k_bin/hlk301.x
INCLUDE_DIR := $(XDEV68K_DIR)/include/xc

BUILD_DIR := build
OBJECT := $(BUILD_DIR)/hello.o
LIST := $(BUILD_DIR)/link.lst
TARGET := $(BUILD_DIR)/HELLO.X

.PHONY: all clean

all: $(TARGET)

$(BUILD_DIR):
	mkdir -p $@

$(OBJECT): hello.s | $(BUILD_DIR)
	$(HAS) -e -u -w0 -I$(INCLUDE_DIR) -o $@ $<

$(LIST): $(OBJECT)
	printf '$(OBJECT)\n' > $@

$(TARGET): $(OBJECT) $(LIST)
	$(HLK) -o $@ -i $(LIST)
	@test -s $@ || (echo "HLK did not create $@" >&2; false)

clean:
	rm -rf $(BUILD_DIR)
