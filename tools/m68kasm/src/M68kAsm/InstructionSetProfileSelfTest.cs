namespace M68kAsm;

internal static class InstructionSetProfileSelfTest
{
    public static bool Run(out string failure)
    {
        if (!Matches(
            """
            [start
              abcd.b d0.b,d1.b
              abcd.b -(a0),-(a1)
              add.b d0.b,d1.b
              add.w 4(a0),d1.w
              add.l d1.l,(a0)
              adda.w d0.w,a1
              adda.l #$12345678,a2
              addi.b #$12,d0.b
              addi.w #$1234,(a0)
              addq.l #8,a0
              addx.b d0.b,d1.b
              addx.w -(a0),-(a1)
              and.b d0.b,d1.b
              and.w (a0),d1.w
              and.l d1.l,(a0)
              andi.b #$12,d0.b
              andi.b #$12,ccr
              andi.w #$1234,sr
              asl.b #1,d0.b
              asr.w d1.w,d2.w
              asl.w (a0)
              bhi.w branch_target
              bchg d0.l,d1.l
              bchg #3,(a0)
              bclr d0.l,(a0)
              bset #7,(a0)
              btst #31,d0.l
              btst d1.l,$54(pc)
              chk.w (a0),d1.w
              clr.b d0.b
              clr.w (a0)
              cmp.b d0.b,d1.b
              cmpa.w (a0),a1
              cmpi.l #$12345678,d0.l
              cmpm.w (a0)+,(a1)+
              dbne d0.w,branch_target
              divs.w (a0),d1.l
              divu.w #3,d2.l
              eor.b d0.b,d1.b
              eor.w d1.w,(a0)
              eori.b #$12,d0.b
              eori.b #$12,ccr
              eori.w #$1234,sr
              exg.l d0.l,d1.l
              exg.l a0,a1
              exg.l d0.l,a1
              ext.w d0.w
              ext.l d1.l
              illegal
              jmp (a0)
              jsr 4(a0)
              lea.l 4(a0),a1
              link.w a6,#-16
              lsl.l #8,d0.l
              lsr.w d1.w,d2.w
              move.b d0.b,d1.b
              move.w a0,d1.w
              move.l #$12345678,(a0)
              movea.w d0.w,a1
              move.w d0.w,ccr
              move.w sr,d1.w
              move.w d0.w,sr
              move.l a0,usp
              move.l usp,a1
              movem.w d0-d3/a0,(a1)
              movem.l (a1)+,d0-d3/a0
              movep.w d0.w,4(a0)
              movep.l 4(a0),d1.l
              moveq.l #-1,d0.l
              muls.w (a0),d1.l
              mulu.w #3,d2.l
              nbcd.b d0.b
              neg.b d0.b
              negx.w (a0)
              nop
              not.l d0.l
              or.b d0.b,d1.b
              or.w (a0),d1.w
              or.l d1.l,(a0)
              ori.b #$12,d0.b
              ori.b #$12,ccr
              ori.w #$1234,sr
              pea.l 4(a0)
              reset
              rol.b #1,d0.b
              ror.w d1.w,d2.w
              roxl.w (a0)
              roxr.l #8,d0.l
              rte
              rtr
              rts
              sbcd.b d0.b,d1.b
              sbcd.b -(a0),-(a1)
              sne.b d0.b
              seq.b (a0)
              stop.w #$2700
              sub.b d0.b,d1.b
              sub.w 4(a0),d1.w
              sub.l d1.l,(a0)
              suba.w d0.w,a1
              subi.b #$12,d0.b
              subq.l #8,a0
              subx.b d0.b,d1.b
              subx.w -(a0),-(a1)
              swap.l d0.l
              tas.b (a0)
              trap #15
              trapv
              tst.b d0.b
              tst.l (a0)
              unlk a6
            branch_target:
              nop
            ]/
            """,
            CpuModel.Mc68000,
            "C300C308D200D2680004D390D2C0D5FC1234567806000012065012345088D300D348C200C250C39002000012023C0012027C1234E300E262E1D0620000EC014108500003019008D000070800001F033A0004439042004250B200B2D00C8012345678B34856C800C283D084FC0003B101B3500A0000120A3C00120A7C1234C141C149C189488048C14AFC4ED04EA8000443E800044E56FFF0E188E26A1200320820BC12345678324044C040C146C04E604E694891010F4CD9010F018800040348000470FFC3D0C4FC00034800440040504E71468082008250839000000012003C0012007C1234486800044E70E318E27AE5D0E0904E734E774E758300830856C057D04E722700920092680004939092C00400001251889300934848404AD04E4F4E764A004A904E5E4E71",
            out failure))
        {
            return false;
        }

        if (!Matches(
            """
            [start
              bkpt #3
              move.w ccr,d0.w
              movec sfc,d0.l
              movec d1.l,dfc
              movec usp,a0
              movec a1,vbr
              moves.b d0.b,(a0)
              moves.w (a0),d1.w
              moves.l a1,4(a0)
              rtd #8
            ]/
            """,
            CpuModel.Mc68010,
            "484B42C04E7A00004E7B10014E7A88004E7B98010E1008000E5010000EA8980000044E740008",
            out failure))
        {
            return false;
        }

        if (!Matches(
            """
            [start
              bfchg (a0){1:8}
              bfclr (a0){d1.l:d2.l}
              bfexts (a0){1:8},d0.l
              bfextu d0.l{d1.l:8},d2.l
              bfffo (a0){1:d2.l},d3.l
              bfins d4.l,(a0){1:8}
              bfset (a0){1:8}
              bftst $24(pc){1:8}
              callm #3,(a0)
              cas.b d0.b,d1.b,(a0)
              cas.w d2.w,d3.w,4(a0)
              cas2.w d0.w:d1.w,d2.w:d3.w,(a0):(a1)
              chk.l (a0),d0.l
              chk2.w (a0),d0.w
              cmp2.l (a0),a1
              divs.l (a0),d0.l
              divsl.l (a0),d1.l:d0.l
              divu.l (a0),d2.l
              divul.l (a0),d3.l:d2.l
              extb.l d0.l
              link.l a6,#-$123456
              movec cacr,d0.l
              movec d1.l,caar
              movec msp,a0
              muls.l (a0),d0.l
              muls.l (a0),d1.l:d0.l
              mulu.l (a0),d2.l
              mulu.l (a0),d3.l:d2.l
              pack d0.w,d1.w,#$12
              pack -(a0),-(a1),#$34
              rtm d0.l
              rtm a1
              trapne
              trapne.w #$1234
              trapne.l #$12345678
              unpk d0.w,d1.w,#$12
              unpk -(a0),-(a1),#$34
              bra.l branch_target
            branch_target:
              nop
            ]/
            """,
            CpuModel.Mc68020,
            "EAD00048ECD00862EBD00048E9C02848EDD03062EFD04048EED00048E8FA0048000406D000030AD000400CE800C200040CFC808090C1411002D0080004D090004C5008004C5008014C5020024C50200349C0480EFFEDCBAA4E7A00024E7B18024E7A88034C1008004C100C014C1020024C102403834000128348003406C006C956FC56FA123456FB12345678838000128388003460FF000000044E71",
            out failure))
        {
            return false;
        }

        if (!Matches(
            """
            [start
              move.l 4(a0,d0.w*2),d1.l
              move.l 300(a0,d0.l*4),d1.l
              move.l ([16,a0,d0.w*2],32),d1.l
              move.l ([16,a0],d0.w*2,32),d1.l
              move.l ([$2c,pc,d0.w*2],32),d1.l
              move.l ([$34,pc],d0.w*2,32),d1.l
            ]/
            """,
            CpuModel.Mc68020,
            "2230020422300D20012C22300322001000202230032600100020223B032200100020223B032600100020",
            out failure))
        {
            return false;
        }

        if (!Matches(
            """
            [start
              pflusha
              pflush #1,#7
              pflush d0,#3,(a0)
              pflush sfc,#7
              pflush dfc,#7,(a1)
              ploadr #2,(a0)
              ploadw d3,4(a0)
              ploadr sfc,(a1)
              ploadw dfc,(a2)
              pmove tc,(a0)
              pmove (a0),tc
              pmove srp,(a0)
              pmove (a0),crp
              pmove tt0,(a0)
              pmove (a0),tt1
              pmove mmusr,(a0)
              pmove (a0),mmusr
              pmovefd (a0),tc
              ptestr #1,(a0),#0
              ptestw d2,(a1),#3,a2
            ]/
            """,
            CpuModel.Mc68030,
            "F0002400F00030F1F0103868F00030E0F01138E1F0102212F028200B0004F0112200F0122001F0104200F0104000F0104A00F0104C00F0100A00F0100C00F0106200F0106000F0104100F0108211F0118D4A",
            out failure))
        {
            return false;
        }

        if (!Matches(
            """
            [start
              tst.w a0
              tst.l a1
              tst.b #1
              tst.w $0e(pc)
            ]/
            """,
            CpuModel.Mc68020,
            "4A484A894A3C00014A7A0004",
            out failure))
        {
            return false;
        }

        if (!Rejected("[start\n  bkpt #0\n]/", CpuModel.Mc68000, out failure)
            || !Rejected("[start\n  bfchg (a0){1:8}\n]/", CpuModel.Mc68010, out failure)
            || !Rejected("[start\n  pflusha\n]/", CpuModel.Mc68020, out failure)
            || !Rejected("[start\n  callm #0,(a0)\n]/", CpuModel.Mc68030, out failure)
            || !Rejected("[start\n  tst.w a0\n]/", CpuModel.Mc68000, out failure))
        {
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool Matches(string source, CpuModel cpu, string expectedHex, out string failure)
    {
        var parse = new LanguageParser().Parse(source);
        if (!parse.Success)
        {
            failure = $"MC{cpu.ToCommandLineName()}フィクスチャのパース失敗: {string.Join(" | ", parse.Diagnostics)}";
            return false;
        }

        var assembly = new BinaryAssembler().Assemble(parse.Module, cpu: cpu);
        if (!assembly.Success)
        {
            failure = $"MC{cpu.ToCommandLineName()}フィクスチャのアセンブル失敗: {string.Join(" | ", assembly.Diagnostics)}";
            return false;
        }

        var actualHex = Convert.ToHexString(assembly.Bytes);
        if (!actualHex.Equals(expectedHex, StringComparison.Ordinal))
        {
            failure = $"MC{cpu.ToCommandLineName()}フィクスチャ不一致: expected={expectedHex}, actual={actualHex}";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool Rejected(string source, CpuModel cpu, out string failure)
    {
        var parse = new LanguageParser().Parse(source);
        if (!parse.Success)
        {
            failure = $"CPU拒否フィクスチャ自体のパース失敗: {string.Join(" | ", parse.Diagnostics)}";
            return false;
        }

        var assembly = new BinaryAssembler().Assemble(parse.Module, cpu: cpu);
        if (assembly.Success)
        {
            failure = $"MC{cpu.ToCommandLineName()}が非対応命令を受理しました: {source.Replace('\n', ' ')}";
            return false;
        }

        failure = string.Empty;
        return true;
    }
}
