#!/usr/bin/python -u

import cPickle
import sys
import commands
import os
import platform
import subprocess

if sys.version_info < (2,6):
    from sets import Set
    set = Set


if sys.version_info < (2,6):
    import md5 as cake_hasher
else:
    import hashlib as cake_hasher


BINDIR="bin/"
OBJDIR=""
verbose = False
debug = False

class OrderedSet:
    """A set that preserves the order of insertion"""

    def __init__(self, init = ()):
        self.ordered = []
        self.unordered = {}

        for s in init:
            self.insert(s)

    def insert(self, e):
        if e in self.unordered:
            return
        self.ordered.append(e)
        self.unordered[e] = True
    
    def __repr__(self):
        return repr(self.ordered)
        
    def __contains__(self, e):
        return self.unordered.__contains__(e)
        
    def __len__(self):
        return self.ordered.__len__()
        
    def __iter__(self):
        return self.ordered.__iter__()
             
        


class UserException (Exception):
    def __init__(self, text):
        Exception.__init__(self, text)



def environ(variable, default):
    if default is None:
        if not variable in os.environ:
            raise UserException("Couldn't find required environment variable " + variable)
        return os.environ[variable]
    else:
        if not variable in os.environ:
            return default
        else:
            return os.environ[variable]

def parse_etc(config_file):
	"""parses /etc/cake as if it was part of the environment.
	os.environ has higher precedence
	"""

	if not os.path.exists(config_file):
		raise UserException("Trying to parse config file. Could not find " + config_file)

	f = open(config_file)
	lines = f.readlines()
	f.close()

	for l in lines:
		if l.startswith("#"):
			continue
		l = l.strip()

		if len(l) == 0:
			continue
		key = l[0:l.index("=")].strip()
		value = l[l.index("=") + 1:].strip()

		for k in os.environ:
			value = value.replace('"', "")
			value = value.replace("$" + k, os.environ[k])
			value = value.replace("${" + k + "}", os.environ[k])

		if not key in os.environ:
			os.environ[key] = str(value)


usage_text = """

Usage: cake [compilation args] filename.cpp [app args]

cake generates and runs C and C++ executables with almost no configuration. To build a C or C++ program, type "cake filename.c" or "cake filename.cpp". 
Cake uses the header includes to determine what other implementation (c,cpp) files are also required to be built and linked against.
Cake also recognises that developers need to build different variants of the same executable.  A variant is defined to be a compiler and optimisation combination.
Examples of variants are gcc46_release and clang_debug.

Source annotations: 
    Embed these magic comments in your hpp and cpp files to give cake instructions on compilation and link flags.

     //#CXXFLAGS=<flags>         Appends the given options to the compile step.
     //#LINKFLAGS=<flags>        Appends the given options to the link step
     //#GCC44_CXXFLAGS=<flags>   Appends the given options to the compile step when building with gcc 4.4.
     //#GCC44_LINKFLAGS=<flags>  Appends the given options to the link step when building with gcc 4.4
     
     If no variant specific annotations are found, then the global variants are also
     searched. This allows default behaviour to be specified, while allowing
     for a particular variant as well.

Environment:
    Environment variables can also be set, from lowest to highest priority, in /etc/cake.conf, ~/.cake.conf or directly in the shell.

    CAKE_DEFAULT_VARIANT       Sets the default variant to use if --variant=<some variant> is not specified on the command line
    CAKE_<variant>_ID          Sets the prefix to the embedded source annotations and predefined build macro.
    CAKE_<variant>_CPP         Sets the C preprocessor command.
    CAKE_<variant>_CC          Sets the C compiler command.
    CAKE_<variant>_CXX         Sets the C++ compiler command
    CAKE_<variant>_LINKER      Sets the linker command.
    CAKE_<variant>_CPPFLAGS    Sets the preprocessor flags for all c and cpp files in the build.
    CAKE_<variant>_CFLAGS      Sets the compilation flags for all c files in the build.
    CAKE_<variant>_CXXFLAGS    Sets the compilation flags for all cpp files in the build.
    CAKE_<variant>_LINKFLAGS   Sets the flags used while linking.
    CAKE_<variant>_TESTPREFIX  Sets the execution prefix used while running unit tests.
    CAKE_<variant>_POSTPREFIX  Sets the execution prefix used while running post-build commands.
    CAKE_BINDIR                Sets the directory where all binary files will be created.
    CAKE_OBJDIR                Sets the directory where all object files will be created.
    CAKE_PROJECT_VERSION_CMD   Sets the command to execute that will return the version number of the project being built. cake then sets a macro equal to this version.
    

Options:

    --help                 Shows this message.
    --quiet                Doesn't output progress messages.
    --verbose              Outputs the result of build commands (doesn't run make with -s)
    --cake-debug           Output extra cake specific info.
    --config               Specify the config file to use.

    --bindir               Specifies the directory to contain binary executable outputs. Defaults to 'bin'.
    --objdir               Specifies the directory to contain object intermediate files. Defaults to 'bin/obj'.
    --generate             Only runs the makefile generation step, does not build.
    --build                Builds the given targets (default).
    --file-list            Print list of referenced files.
    --output=<filename>    Overrides the output filename.
    --variant=<vvv>        Reads the CAKE_<vvv>_CC, CAKE_<vvv>_CXXFLAGS and CAKE_<vvv>_LINKFLAGS
                           environment variables to determine the build flags. 
                           Examples of variants are debug, release, gcc44_debug, gcc46_release.
    --static-library       Build a static library rather than executable.  This is an alias for --LINKER="ar -src"
    --dynamic-library      Build a dynamic library rather than executable.  This is an alias for --append-LINKFLAGS="-shared"
    
    --ID=<id>              Sets the prefix to the embedded source annotations, and a predefined macro CAKE_${ID}
    --CPP=<preprocessor>   Sets the C preprocessor command.
    --CC=<compiler>        Sets the C compiler command.
    --CXX=<compiler>       Sets the C++ compiler command.
    --LINKER=<linker>      Sets the linker command.
    
    --CPPFLAGS=<flags>     Sets the preprocessor flags for all c and cpp files in the build.
    --CFLAGS=<flags>       Sets the compilation flags for all c files in the build.
    --CXXFLAGS=<flags>     Sets the compilation flags for all cpp files in the build.
    --LINKFLAGS=<flags>    Sets the flags used while linking.
    --TESTPREFIX=<cmd>     Runs tests with the given prefix, eg. "valgrind --quiet --error-exitcode=1"
    --POSTPREFIX=<cmd>     Runs post execution commands with the given prefix, eg. "timeout 60"
    
    --append-CPPFLAGS=...  Appends the given text to the CPPFLAGS already set.   Useful for adding search paths etc.
    --append-CFLAGS=...    Appends the given text to the CFLAGS already set. Useful for adding search paths etc.
    --append-CXXFLAGS=...  Appends the given text to the CXXFLAGS already set. Useful for adding search paths etc.
    --append-LINKFLAGS=..  Appends the given text to the LINKFLAGS already set. Use for example with `wx-config --libs`

    --bindir=...           Overrides the directory where binaries are produced. 'bin/' by default.
    --project-version-cmd=...  Sets the command to execute that will return the version number of the project being built.

    --include-git-root     Walk up directory path to find .git directory. If found, add path as an include path. 
                           This is enabled by default. 
                           
    --no-git-root          Disable the git root include. 

    --begintests           Starts a test block. The cpp files following this declaration will
                           generate executables which are then run.

    --endtests             Ends a test block.

    --beginpost            Starts a post execution block. The commands given after this will be
                           run verbatim after each build. Useful for running integration tests,
                           or generating tarballs, uploading to a website etc.
    --endpost              Ends a post execution block.

Examples:

This command-line generates bin/prime-factoriser and bin/frobnicator in release mode.
It also generates several tests into the bin directory and runs them. If they are
all successful, integration_test.sh is run.

   cake apps/prime-factoriser.cpp apps/frobnicator.cpp --begintests tests/*.cpp --endtests --beginpost ./integration_test.sh --variant=release
   
To build a static  library of the get_numbers.cpp file in the example tests   

   cake --static-library tests/get_numbers.cpp
   
To build a dynamic library of the get_numbers.cpp file in the example tests

    cake --dynamic-library tests/get_numbers.cpp

"""


def usage(msg = ""):
    if len(msg) > 0:
        print >> sys.stderr, msg
        print >> sys.stderr, ""

    print usage_text.strip() + "\n"

    sys.exit(1)


def printCakeVariables():
    print "  ID        : " + CAKE_ID
    print "  VARIANT   : " + Variant
    print "  CPP       : " + CPP
    print "  CC        : " + CC
    print "  CXX       : " + CXX
    print "  LINKER    : " + LINKER
    print "  CPPFLAGS  : " + CPPFLAGS
    print "  CFLAGS    : " + CFLAGS
    print "  CXXFLAGS  : " + CXXFLAGS
    print "  LINKFLAGS : " + LINKFLAGS    
    print "  TESTPREFIX: " + TESTPREFIX
    print "  POSTPREFIX: " + POSTPREFIX
    print "  BINDIR    : " + BINDIR
    print "  OBJDIR    : " + OBJDIR
    print "  PROJECT_VERSION_CMD : " + PROJECT_VERSION_CMD
    print "\n"


def extractOption(text, option):
    """Extracts the given option from the text, returning the value
    on success and the trimmed text as a tuple, or (None, originaltext)
    on no match.
    """

    try:
        length = len(option)
        start = text.index(option)
        end = text.index("\n", start + length)
        
        result = text[start + length:end]
        trimmed = text[:start] + text[end+1:]
        return result, trimmed
        
    except ValueError:
        return None, text


realpath_cache = {}
def realpath(x):
    global realpath_cache
    #print "BEGIN ",x
    if not x in realpath_cache:
        realpath_cache[x] = os.path.realpath(x)
    return realpath_cache[x]


def munge(to_munge):
    if isinstance(to_munge, dict):
        if len(to_munge) == 1:
            return OBJDIR + "@@".join([realpath(x) for x in to_munge]).replace("/", "@")
        else:
            return OBJDIR + cake_hasher.md5(str([realpath(x) for x in to_munge])).hexdigest()
    else:
        return OBJDIR + realpath(to_munge).replace("/", "@")


def force_get_dependencies_for(deps_file, source_file, quiet, verbose):
    """Recalculates the dependencies and caches them for a given source file"""

    global CAKE_ID
    
    if not quiet:
        print "... " + source_file + " (dependencies)"
    
    cmd = CPP + CPPFLAGS + " -DCAKE_DEPS -MM -MF " + deps_file + ".tmp " + source_file

    if verbose:
        print cmd

    status, output = commands.getstatusoutput(cmd)
    if status != 0:
        raise UserException(cmd + "\n" + output)

    f = open(deps_file + ".tmp")
    text = f.read()
    f.close()
    os.unlink(deps_file + ".tmp")

    files = text.split(":")[1]
    files = files.replace("\\", " ").replace("\t"," ").replace("\n", " ")
    files = [x for x in files.split(" ") if len(x) > 0]
    files = list(set([realpath(x) for x in files]))
    files.sort()

    headers = [realpath(h) for h in files if h.endswith(".hpp") or h.endswith(".h")]
    sources = [realpath(h) for h in files if h.endswith(".cpp") or h.endswith(".c")]

    # determine cflags, cxxflags and linkflags
    cflags = {}
    cxxflags = {}
    linkflags = OrderedSet()

    explicit_c = "//#" + CAKE_ID + "_CFLAGS="
    explicit_cxx = "//#" + CAKE_ID + "_CXXFLAGS="
    explicit_link = "//#" + CAKE_ID + "_LINKFLAGS="

    explicit_plat_c = "//#" + platform.system() + "_CFLAGS="
    explicit_plat_cxx = "//#" + platform.system() + "_CXXFLAGS="
    explicit_plat_link = "//#" + platform.system() + "_LINKFLAGS="

    explicit_glob_c = "//#CFLAGS="
    explicit_glob_cxx = "//#CXXFLAGS="
    explicit_glob_link = "//#LINKFLAGS="

    for h in headers + [source_file]:
        path = os.path.split(h)[0]
        f = open(h)

        # reading and handling as one string is slightly faster then
        # handling a list of strings
        text = f.read(2048)

        found = False

        # first check for variant specific flags
        if len(CAKE_ID) > 0:
            while True:
                result, text = extractOption(text, explicit_c)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_c + " = '" + result + "' for " + h
                    result = result.replace("${path}", path)
                    cflags[result] = True
                    found = True
            while True:
                result, text = extractOption(text, explicit_cxx)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_cxx + " = '" + result + "' for " + h
                    result = result.replace("${path}", path)
                    cxxflags[result] = True
                    found = True
            while True:
                result, text = extractOption(text, explicit_link)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_link + " = '" + result + "' for " + h
                    linkflags.insert(result.replace("${path}", path))
                    found = True

        # If not found, check for system specific flags
        if not found:
            while True:
                result, text = extractOption(text, explicit_plat_c)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_plat_c + " = '" + result + "' for " + h
                    result = result.replace("${path}", path)
                    cflags[result] = True
                    found = True
            while True:
                result, text = extractOption(text, explicit_plat_cxx)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_plat_cxx + " = '" + result + "' for " + h
                    result = result.replace("${path}", path)
                    cxxflags[result] = True
                    found = True
            while True:
                result, text = extractOption(text, explicit_plat_link)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_plat_link + " = '" + result + "' for " + h
                    linkflags.insert(result.replace("${path}", path))
                    found = True

        # if none, then check globals
        if not found:
            while True:
                result, text = extractOption(text, explicit_glob_c)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_glob_c + " = '" + result + "' for " + h
                    result = result.replace("${path}", path)
                    cflags[result] = True
            while True:
                result, text = extractOption(text, explicit_glob_cxx)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_glob_cxx + " = '" + result + "' for " + h
                    result = result.replace("${path}", path)
                    cxxflags[result] = True                    
            while True:
                result, text = extractOption(text, explicit_glob_link)
                if result is None:
                    break
                else:
                    if debug:
                        print "explicit " + explicit_glob_link + " = '" + result + "' for " + h
                    linkflags.insert(result.replace("${path}", path))

        f.close()
        pass

    # cache
    f = open(deps_file, "w")
    cPickle.dump((headers, sources, cflags, cxxflags, linkflags), f)
    f.close()
    if deps_file in stat_cache:
        del stat_cache[deps_file]
    if deps_file in realpath_cache:
        del realpath_cache[deps_file]
    
    return headers, sources, cflags, cxxflags, linkflags


stat_cache = {}
def stat(f):
    if not f in stat_cache:
        try:
            stat_cache[f] = os.stat(f)
        except OSError:
            stat_cache[f] = None
    return stat_cache[f]

dependency_cache = {}


def get_dependencies_for(source_file, quiet, verbose):
    """Converts a gcc make command into a set of headers and source dependencies"""    
    
    global dependency_cache

    if source_file in dependency_cache:
        return dependency_cache[source_file]

    deps_file = munge(source_file) + ".deps"

    # try and reuse the existing if possible
    deps_stat = stat(deps_file)
    if deps_stat:
        deps_mtime = deps_stat.st_mtime
        all_good = True
        
        try:
            f = open(deps_file)            
            headers, sources, cflags, cxxflags, linkflags  = cPickle.load(f)
            f.close()
        except:
            all_good = False

        if all_good:
            for s in headers + [source_file]:
                try:
                    if stat(s).st_mtime > deps_mtime:
                        all_good = False
                        break
                except: # missing file counts as a miss
                    all_good = False
                    break
        if all_good:
            result = headers, sources, cflags, cxxflags, linkflags
            dependency_cache[source_file] = result
            return result

    # failed, regenerate dependencies
    result = force_get_dependencies_for(deps_file, source_file, quiet, verbose)
    dependency_cache[source_file] = result
    return result


def insert_dependencies(sources, ignored, new_file, linkflags, cause, quiet, verbose, file_list):
    """Given a set of sources already being compiled, inserts the new file."""
    
    if not new_file.startswith("/"):
        raise Exception("bad")    
    
    if new_file in sources:
        return
        
    if new_file in ignored:
        return
        
    if stat(new_file) is None:
        ignored.append(new_file)
        return

    # recursive step
    new_headers, new_sources, newcflags, newcxxflags, newlinkflags = get_dependencies_for(new_file, quiet, verbose)
    
    sources[realpath(new_file)] = (newcflags, newcxxflags, cause, new_headers)
    file_list.insert(new_file)
    
    # merge in link options
    for l in newlinkflags:
        linkflags.insert(l)
    
    copy = cause[:]
    copy.append(new_file)
    
    for h in new_headers:
        insert_dependencies(sources, ignored, os.path.splitext(h)[0] + ".cpp", linkflags, copy, quiet, verbose, file_list)
        insert_dependencies(sources, ignored, os.path.splitext(h)[0] + ".c", linkflags, copy, quiet, verbose, file_list)

    for s in new_sources:
        insert_dependencies(sources, ignored, s, linkflags, copy, quiet, verbose, file_list)


def try_set_variant(variant,static_library):
    global Variant, CAKE_ID, CPP, CC, CXX, LINKER, CPPFLAGS, CFLAGS, CXXFLAGS, LINKFLAGS, TESTPREFIX, POSTPREFIX
    Variant = "CAKE_" + variant.upper()
    
    CAKE_ID = environ(Variant + "_ID", "")
    CPP = environ(Variant + "_CPP", None)
    CC = environ(Variant + "_CC", None)
    CXX = environ(Variant + "_CXX", None)
    if static_library:
        LINKER = "ar -src"
    else:
        LINKER = environ(Variant + "_LINKER", None)
        
    CPPFLAGS = environ(Variant + "_CPPFLAGS", None)    
    CFLAGS = environ(Variant + "_CFLAGS", None)
    CXXFLAGS = environ(Variant + "_CXXFLAGS", None)
    LINKFLAGS = environ(Variant + "_LINKFLAGS", None)
    TESTPREFIX = environ(Variant + "_TESTPREFIX", None)
    POSTPREFIX = environ(Variant + "_POSTPREFIX", None)

def lazily_write(filename, newtext):
    oldtext = ""
    try:
        f = open(filename)
        oldtext = f.read()
        f.close()
    except:
        pass        
    if newtext != oldtext:
        f = open(filename, "w")
        if filename in stat_cache:
            del stat_cache[filename]
        if filename in realpath_cache:
            del realpath_cache[filename]
        f.write(newtext)
        f.close()


ignore_option_mash = [ '-fprofile-generate', '-fprofile-use' ]
def objectname(source, entry):
    cflags, cxxflags, cause, headers = entry
    mash_name = "".join(cflags) + " " + CFLAGS + " " + "".join(cxxflags) + " " + CXXFLAGS + " "

    if source.endswith(".c"):
        mash_name += CC
    else:
        mash_name += CXX

    o = mash_name.split();
    o.sort()
    mash_inc = ""

    for s in o:
        if not s in ignore_option_mash:
            mash_inc += s
        else:
            mash_inc += 'ignore'

    h = cake_hasher.md5( mash_inc ).hexdigest()
    return munge(source) + str(len(str(mash_inc))) + "-" + h + ".o"



def generate_rules(source, output_name, generate_test, makefilename, quiet, verbose, static_library, file_list):
    """
    Generates a set of make rules for the given source.
    If generate_test is true, also generates a test run rule.
    """

    global Variant

    rules = {}
    sources = {}
    ignored = []
    linkflags = OrderedSet()
    cause = []
    
    source = realpath(source)
    file_list.insert( source )
    insert_dependencies(sources, ignored, source, linkflags, cause, quiet, verbose, file_list)
    
    # compile rule for each object
    for s in sources:
        obj = objectname(s, sources[s])
        cflags, cxxflags, cause, headers = sources[s]
        
        for h in headers:
            file_list.insert( h )

        definition = []
        definition.append(obj + " : " + " ".join(headers + [s]))
        if not quiet:
            definition.append("\t" + "@echo ... " + s)
        if s.endswith(".c"):
            definition.append("\t" + CC + " " + CFLAGS + " " + " ".join(cflags) + " -c " + " " + s + " " " -o " + obj)
        else:
            definition.append("\t" + CXX + " " + CXXFLAGS + " " + " ".join(cxxflags) + " -c " + " " + s + " " " -o " + obj)

        rules[obj] = "\n".join(definition)

    # link rule
    definition = []
    tmp_output_name = OBJDIR + Variant + "/" + os.path.split(output_name)[-1]
    definition.append( tmp_output_name + " : " + " ".join([objectname(s, sources[s]) for s in  sources]) + " " + makefilename)
    linker_line = "\t" + LINKER + " "  
    if not static_library:
        linker_line += "-o "
    linker_line +=  tmp_output_name + " " + " " .join([objectname(s, sources[s]) for s in  sources])  + " " 
    if not static_library:
        linker_line += LINKFLAGS + " " + " ".join(linkflags)
    definition.append( linker_line )    
        
    definition.append( "\n.PHONY : " + output_name )
    definition.append( "\n" + output_name + " : " + tmp_output_name )
    if not quiet:
        definition.append("\t" + "@echo ... " + output_name)
    definition.append( "\trm -f " + output_name )
    definition.append( "\tcp " + tmp_output_name + " " + output_name )

    rules[output_name] = "\n".join(definition)

    if generate_test:
        definition = []
        test = munge(output_name) + ".result"
        definition.append( test + " : " + tmp_output_name )
        if not quiet:
            definition.append("\t" + "@echo ... test " + output_name)

        t = ""
        if TESTPREFIX != "":
            t = TESTPREFIX + " "
        definition.append( "\t" + "rm -f " + test + " && " + t + tmp_output_name + " && touch " + test)
        rules[test] = "\n".join(definition)

    return rules


def render_makefile(makefilename, rules):
    """Renders a set of rules as a makefile"""
    
    rules_as_list = [rules[r] for r in rules]
    rules_as_list.sort()
    
    objects = [r for r in rules]
    objects.sort()
    
    # top-level build rule
    text = []
    text.append("all : " + " ".join(objects))
    text.append("")
    
    for rule in rules_as_list:
        text.append(rule)
        text.append("")        
    
    text = "\n".join(text)
    lazily_write(makefilename, text)


def cpus():
    if platform.system() == 'Linux':
        f = open("/proc/cpuinfo")
        t = [x for x in f.readlines() if x.startswith("processor")]
        f.close()
        return str(len(t))
    elif platform.system() == 'Darwin':
        return str(int(subprocess.check_output("sysctl -n hw.ncpu", shell=True)))
    else:
        raise "Can not detect platform type, aborting"

def do_generate(source_to_output, tests, post_steps, quiet, verbose, static_library, file_list):
    """Generates all needed makefiles"""
    global Variant

    all_rules = {}
    for source in source_to_output:
        makefilename = munge(source) + "." + Variant + ".Makefile"
        rules = generate_rules(source, source_to_output[source], source_to_output[source] in tests, makefilename, quiet, verbose, static_library, file_list)
        all_rules.update(rules)
        render_makefile(makefilename, rules)

    combined_filename = munge(source_to_output) + "." + Variant + ".combined.Makefile"

    all_previous = [r for r in all_rules]
    previous = all_previous

    post_with_space = POSTPREFIX.strip()
    if len(post_with_space) > 0:
        post_with_space = POSTPREFIX + " "

    for s in post_steps:
        passed = OBJDIR + cake_hasher.md5(s).hexdigest() + ".passed"
        rule = passed + " : " + " ".join(previous + [s]) + "\n"
        if not quiet:
            rule += "\t" + "echo ... post " + post_with_space + s
        rule += "\trm -f " + passed + " && " + post_with_space + s + " && touch " + passed
        all_rules[passed] = rule
        previous =  all_previous + [s]

    render_makefile(combined_filename, all_rules)
    return combined_filename


def do_build(makefilename, verbose):
    cmd="make -r " + {False:"-s ",True:""}[verbose] + "-f " + makefilename + " -j" + cpus()
    if verbose:
        print cmd
    result = os.system(cmd)
    if result != 0:
        print
        print "ERROR: Build failed."
        sys.exit(1)
    elif verbose:
        print
        print "Build successful."
        


def do_run(output, args):
    os.execvp(output, [output] + args)

def find_git_root():
    p = os.path.abspath(".")

    while (p != "/"):
        if (os.path.exists( p + "/.git" )):
            return p
        p = os.path.dirname(p)

    return;


def main(config_file):
    global CAKE_ID, CPP, CC, CXX, LINKER
    global CPPFLAGS, CFLAGS, CXXFLAGS, LINKFLAGS
    global TESTPREFIX, POSTPREFIX, BINDIR, OBJDIR, PROJECT_VERSION_CMD
    global verbose, debug
    global Variant

    if len(sys.argv) < 2:
        usage()

    # parse arguments
    args = sys.argv[1:]
    appargs = []
    nextOutput = None

    generate = True
    build = True
    file_list = False
    quiet = False
    static_library = False
    dynamic_library=False
    to_build = {}
    inTests = False
    inPost = False
    tests = []
    post_steps = []
    include_git_root = True 
    git_root = None
  
    # Initialise the variables to the debug default
    try_set_variant(Variant,static_library)    
    
    # set verbose and check for help
    # copy list so we can remove from the original and still iterate
    for a in list(args):
        if a == "--verbose":
            verbose = True
            args.remove(a)
        elif a == "--cake-debug":
            debug = True
            args.remove(a)
        elif a == "--static-library":
            static_library = True
            LINKER = "ar -src"
            args.remove(a)            
        elif a == "--dynamic-library":
            dynamic_library = True   
            LINKFLAGS += " -shared"         
            args.remove(a)                        
        elif a == "--help":
            usage()
            return
        elif a == "--version":
            # TODO: somehow extract this from git
            print """cake 3.0"""
            return

    # deal with variant next
    # to set the base set of flags for the other options to apply to
    # copy list so we can remove from the original and still iterate
    for a in list(args):
        if a.startswith("--variant="):
            variant = a[a.index("=")+1:]
            if variant.upper() in ["DEBUG","RELEASE","COVERAGE"]:
                variant = CAKE_ID + "_" + variant
            try_set_variant(variant,static_library)
            args.remove(a)
            continue

    for a in args:
        if a.startswith("--config="):
            config_file = a[a.index("=")+1:]
            continue;

        if a.startswith("--ID="):
            CAKE_ID = a[a.index("=")+1:]
            continue
            
        if a.startswith("--CPP="):
            CPP = a[a.index("=")+1:]
            continue
            
        if a.startswith("--CC="):
            CC = a[a.index("=")+1:]
            continue
            
        if a.startswith("--CXX="):
            CPP = a[a.index("=")+1:]
            continue
            
        if a.startswith("--LINKER="):
            LINKER = a[a.index("=")+1:]
            continue

        if a.startswith("--CPPFLAGS="):
            CPPFLAGS = " " + a[a.index("=")+1:]
            continue

        if a.startswith("--append-CPPFLAGS="):
            CPPFLAGS += " " + a[a.index("=")+1:]
            continue

        if a.startswith("--CFLAGS="):
            CFLAGS = " " + a[a.index("=")+1:]
            continue

        if a.startswith("--append-CFLAGS="):
            CFLAGS += " " + a[a.index("=")+1:]
            continue

        if a.startswith("--CXXFLAGS="):
            CXXFLAGS = " " + a[a.index("=")+1:]
            continue

        if a.startswith("--append-CXXFLAGS="):
            CXXFLAGS += " " + a[a.index("=")+1:]
            continue

        if a.startswith("--LINKFLAGS="):
            LINKFLAGS = a[a.index("=")+1:]
            continue

        if a.startswith("--append-LINKFLAGS="):
            LINKFLAGS += " " + a[a.index("=")+1:]
            continue

        if a.startswith("--TESTPREFIX="):
            TESTPREFIX = a[a.index("=")+1:]
            continue

        if a.startswith("--POSTPREFIX="):
            POSTPREFIX = a[a.index("=")+1:]
            continue
            
        if a.startswith("--bindir="):
            BINDIR = a[a.index("=")+1:]
            if not BINDIR.endswith("/"):
                BINDIR = BINDIR + "/"
            continue

        if a.startswith("--objdir="):
            OBJDIR = a[a.index("=")+1:]
            if not OBJDIR.endswith("/"):
                OBJDIR = OBJDIR + "/"
            continue
        
        if a.startswith("--project-version-cmd="):
            PROJECT_VERSION_CMD = a[a.index("=")+1:]
            continue

        if a == "--beginpost":
            if inTests:
                usage("--beginpost cannot occur inside a --begintests block")
            inPost = True
            continue

        if a == "--endpost":
            inPost = False
            continue

        if a == "--begintests":
            if inPost:
                usage("--begintests cannot occur inside a --beginpost block")
            inTests = True
            continue

        if a == "--endtests":
            if not inTests:
                usage("--endtests can only follow --begintests")
            inTests = False
            continue

        if a.startswith("--quiet"):
            quiet = True
            continue

        if a == "--generate":
            generate = True
            build = False
            continue

        if a == "--no-git-root":
            include_git_root = False
            continue
            
        if a == "--include-git-root":
            include_git_root = True
            continue
            
        if a == "--file-list":
            file_list = True
            build = False
            continue

        if a == "--build":
            generate = True
            build = True
            continue

        if a.startswith("--output="):
            nextOutput = a[a.index("=")+1:]
            continue

        if a.startswith("--"):
            usage("Invalid option " + a)            

        if nextOutput is None:
            nextOutput = os.path.splitext(BINDIR + os.path.split(a)[1])[0]
            if static_library:
                nextOutput = os.path.splitext(BINDIR + "lib" + os.path.split(a)[1])[0] + ".a"
            if dynamic_library:
                nextOutput = os.path.splitext(BINDIR + "lib" + os.path.split(a)[1])[0] + ".so"

        if inPost:
            post_steps.append(a)
        else:
            to_build[a] = nextOutput
            if inTests:
                tests.append(nextOutput)
        nextOutput = None

    if include_git_root:
        git_root = find_git_root()
        if (git_root):
            if (verbose):
                print "adding git root " + git_root            
            CPPFLAGS += " -I " + git_root
            CFLAGS += " -I " + git_root
            CXXFLAGS += " -I " + git_root
        else:
            if (verbose):
                print "no git root found"

    if len(Variant) == 0:
        raise "Variant has to be defined before here"
    else:      
        CPPFLAGS += " -DCAKE_VARIANT=\\\"" + Variant + "\\\"" 
        CFLAGS += " -DCAKE_VARIANT=\\\"" + Variant + "\\\"" 
        CXXFLAGS += " -DCAKE_VARIANT=\\\"" + Variant + "\\\"" 

    # default objdir
    if OBJDIR == "":
        OBJDIR = BINDIR

    if len(CAKE_ID) == 0:
        raise "CAKE_ID must be defined before we get to here"
    else:    
        OBJDIR   += CAKE_ID + "/"
        CPPFLAGS += " -DCAKE_" + CAKE_ID
        CFLAGS   += " -DCAKE_" + CAKE_ID
        CXXFLAGS += " -DCAKE_" + CAKE_ID
        
    if len(PROJECT_VERSION_CMD) == 0:
        raise "CAKE_PROJECT_VERSION_CMD must be defined before we get to here"
    else:    
        status, project_version = commands.getstatusoutput(PROJECT_VERSION_CMD)         
        CPPFLAGS += " -DCAKE_PROJECT_VERSION=\\\"" + project_version + "\\\"" 
        CFLAGS   += " -DCAKE_PROJECT_VERSION=\\\"" + project_version + "\\\""
        CXXFLAGS += " -DCAKE_PROJECT_VERSION=\\\"" + project_version + "\\\""                   

    if debug:
        printCakeVariables()

    if len(to_build) == 0:
        usage("You must specify a filename.")

    try:
        os.makedirs(OBJDIR + Variant)
    except:
        pass

    try:
        os.makedirs(BINDIR)
    except:
        pass

    
    for c in to_build.keys()[:]:
        if len(c.strip()) == 0:
            del to_build[c]
            continue

        if not stat(c):
            print >> sys.stderr, c + " is not found."
            sys.exit(1)

    files_referenced = OrderedSet()
    if generate:
        makefilename = do_generate(to_build, tests, post_steps, quiet, verbose, static_library, files_referenced)
        
    if file_list:
        for src in files_referenced:
            if (git_root):
                print os.path.relpath( src, git_root )
            else:
                print src
                #print os.path.relpath( src )

    if build:
        do_build(makefilename, verbose)
    return


try:

    # data
    config_file = os.path.dirname(sys.argv[0]) + "/cake.conf"  # cake.conf file found in the same directory as the cake python script.
    Variant = "gcc46_debug"

    CAKE_ID = "GCC46"     # TODO:  Explain what is the difference between an ID and a variant.  Also a better default probably the users $CC 
    CPP = "g++"      # C and C++ preprocessor
    CC = "g++"       # C compiler
    CXX = "g++"      # C++ compiler
    LINKER = "g++"   # Who would have guessed.  The linker.
 
    CPPFLAGS = ""    # Flags for the C and C++ preprocessor
    CFLAGS = ""      # Flags for C compiler
    CXXFLAGS = ""    # Flags for C++ compiler
    LINKFLAGS = ""   # Flags for the linker
    PROJECT_VERSION_CMD = "echo 888.888.888-1"  # Command to run that will return the version of the project being built 
 
    TESTPREFIX=""    # commands to stick on the front of any tests being run.  e.g., time or set_affinity, etc.
    POSTPREFIX=""    # commands to stick on the front of any post build commands being run
    BINDIR="bin/"    # directory to write the generated executables
    OBJDIR=""        # directory to write any intermediate object files

    # deal with configuration
    # Use configuration in the order (lowest to highest priority)
    # 1) same path as exe, 
    # 2) system config 
    # 3) user config
    # 4) given on the command line
    # 5) environment variables
    system_config = "/etc/cake.conf"
    if os.path.exists(system_config):
        config_file = system_config
    
    user_config=os.path.expanduser("~/.cake.conf")
    if os.path.exists(user_config):
        config_file = user_config
    
    for a in list(sys.argv[1:]):
        if a.startswith("--config="):
            config_file = a[a.index("=")+1:]
            break

    parse_etc( config_file )
    
    Variant = environ("CAKE_DEFAULT_VARIANT", Variant)
    CAKE_ID = environ("CAKE_ID", CAKE_ID)
    CPP = environ("CAKE_CPP", CPP)
    CC = environ("CAKE_CC", CC)
    CXX = environ("CAKE_CXX", CXX)
    LINKER = environ("CAKE_LINKER", LINKER)
    
    CPPFLAGS = environ("CAKE_CPPFLAGS", CPPFLAGS)
    CFLAGS = environ("CAKE_CFLAGS", CFLAGS)
    CXXFLAGS = environ("CAKE_CXXFLAGS", CXXFLAGS)
    LINKFLAGS = environ("CAKE_LINKFLAGS", LINKFLAGS)
    PROJECT_VERSION_CMD = environ("CAKE_PROJECT_VERSION_CMD", LINKFLAGS)
    
    TESTPREFIX = environ("CAKE_TESTPREFIX", TESTPREFIX)
    POSTPREFIX = environ("CAKE_POSTPREFIX", POSTPREFIX)
    BINDIR = environ("CAKE_BINDIR", BINDIR)
    OBJDIR = environ("CAKE_OBJDIR", OBJDIR)
    
    main(config_file)

except SystemExit:
    raise
except IOError,e :
    print >> sys.stderr, str(e)
    sys.exit(1)
except UserException, e:
    print >> sys.stderr, str(e)
    sys.exit(1)
except KeyboardInterrupt:
    sys.exit(1)

