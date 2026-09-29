// Dump every instruction of the current program to a tab-separated cache, in
// listing order, for the Python helpers (insdb.py) to scan without paying a
// JPype crossing per instruction. Native Java: ~600k instructions in seconds.
//
// Args: <outPath> <key>
// Line 1: "#key\t<key>"; then one line per function "F\t<entryHex>\t<name>";
// then one line per instruction:
//   I\t<addrHex>\t<funcEntryHex or ->\t<mnemonic>\t<toString>\t<op0 repr>\t<scalars>
// where <scalars> is a comma list of unsigned hex values from every operand.
// Tabs and newlines inside text fields are replaced by spaces.
import java.io.*;
import java.nio.charset.StandardCharsets;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.*;
import ghidra.program.model.scalar.Scalar;

public class ListingDump extends GhidraScript {

    private static String clean(String s) {
        return s.replace('\t', ' ').replace('\n', ' ').replace('\r', ' ');
    }

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        String tmp = args[0] + ".tmp";
        FunctionManager fm = currentProgram.getFunctionManager();
        Listing listing = currentProgram.getListing();
        try (Writer w = new BufferedWriter(new OutputStreamWriter(
                new FileOutputStream(tmp), StandardCharsets.UTF_8), 1 << 20)) {
            w.write("#key\t" + args[1] + "\n");
            FunctionIterator fit = fm.getFunctions(true);
            while (fit.hasNext()) {
                Function f = fit.next();
                w.write("F\t" + Long.toHexString(f.getEntryPoint().getOffset()) + "\t"
                        + clean(f.getName()) + "\n");
            }
            InstructionIterator it = listing.getInstructions(true);
            StringBuilder sb = new StringBuilder(160);
            while (it.hasNext()) {
                Instruction ins = it.next();
                sb.setLength(0);
                sb.append("I\t").append(Long.toHexString(ins.getAddress().getOffset())).append('\t');
                Function f = fm.getFunctionContaining(ins.getAddress());
                sb.append(f == null ? "-" : Long.toHexString(f.getEntryPoint().getOffset())).append('\t');
                sb.append(ins.getMnemonicString()).append('\t');
                sb.append(clean(ins.toString())).append('\t');
                int nops = ins.getNumOperands();
                sb.append(nops > 0 ? clean(ins.getDefaultOperandRepresentation(0)) : "").append('\t');
                boolean first = true;
                for (int i = 0; i < nops; i++) {
                    for (Object o : ins.getOpObjects(i)) {
                        if (o instanceof Scalar) {
                            if (!first) sb.append(',');
                            sb.append(Long.toHexString(((Scalar) o).getUnsignedValue()));
                            first = false;
                        }
                    }
                }
                sb.append('\n');
                w.write(sb.toString());
            }
        }
        File out = new File(args[0]);
        out.delete();
        if (!new File(tmp).renameTo(out)) {
            throw new IOException("could not rename " + tmp + " to " + args[0]);
        }
    }
}
