#!/usr/bin/perl -w

use strict;

unless ($#ARGV == 2) {
    print STDERR "Usage: $0 profile compiler glob-pattern\n";
    exit 1;
}

#
# Expected value constants
#
my $EXPECTING_WRONG_ERROR = 1;
my $EXPECTING_NO_ERROR    = 2;
my %expecting_map = ();
my %ignore_map = ();

my $profile = $ARGV [0];
my $compile = $ARGV [1];
my $files   = $ARGV [2];

if (open (EXPECT_WRONG, "<$profile-expect-wrong-error")) {
	$expecting_map{$_} = $EXPECTING_WRONG_ERROR 
	foreach map {
		chomp,                     # remove trailing \n
		s/\#.*//g,                 # remove # style comments
		s/\s//g;                    # remove whitespace
		$_ eq "" ? () : $_;        # now copy over non empty stuff
	} <EXPECT_WRONG>;
	
	close EXPECT_WRONG;
}

if (open (EXPECT_NO, "<$profile-expect-no-error")) {
	$expecting_map{$_} = $EXPECTING_NO_ERROR 
	foreach map {
		chomp,                     # remove trailing \n
		s/\#.*//g,                 # remove # style comments
		s/\s//g;                    # remove whitespace
		$_ eq "" ? () : $_;        # now copy over non empty stuff
	} <EXPECT_NO>;
        
	close EXPECT_NO;
}

if (open (IGNORE, "<$profile-ignore-tests")) {
	$ignore_map{$_} = 1
	foreach map {
		chomp,                     # remove trailing \n
		s/\#.*//g,                 # remove # style comments
		s/\s//g;                    # remove whitespace
		$_ eq "" ? () : $_;        # now copy over non empty stuff
	} <IGNORE>;
	
	close IGNORE;
}

my $RESULT_UNEXPECTED_CORRECT_ERROR     = 1;
my $RESULT_CORRECT_ERROR                = 2;
my $RESULT_UNEXPECTED_INCORRECT_ERROR   = 3;
my $RESULT_EXPECTED_INCORRECT_ERROR     = 4;
my $RESULT_UNEXPECTED_NO_ERROR          = 5;
my $RESULT_EXPECTED_NO_ERROR            = 6;
my $RESULT_UNEXPECTED_CRASH		= 7;

my @statuses = (
	"UNEXPECTED TEST HARNESS INTERNAL ERROR",
	"OK (still listed as a failure)",
	"OK",
	"REGRESSION: An incorrect error was reported",
	"KNOWN ISSUE: Incorrect error reported",
	"REGRESSION: No error reported", 
	"KNOWN ISSUE: No error reported",
	"UNEXPECTED CRASH"
);

my @status_items = (
	[],  # should be empty
	[],
	[],
	[],
	[],
	[],
	[],
	[],
);

my %results_map = ();
my $total = 0;
my $textmsg = "";

open (PROFILELOG, ">$profile.log") or die "Cannot open log file $profile.log";

foreach (glob ($files)) {
        my $tname = $_;
	my ($error_number) = (/[a-z]*(\d+)(-\d+)?\.cs/);
	my $options = `sed -n 's,^// Compiler options:,,p' $_`;
	chomp $options;

	if (exists $ignore_map {$_}) {
	    print "IGNORED: $_\n";
	    print PROFILELOG "IGNORED: $_\n";
	    next;
	}

        $total++;
	my $testlogfile="$profile-$_.log";
	system "$compile --expect-error $error_number $options -out:$profile-$_.junk $_ > $testlogfile 2>&1";
	
	exit 1 if $? & 127;
	
	my $exit_value = $? >> 8;

	my $status;
	
	if ($exit_value == 0) {
                system "rm -f $testlogfile";
		$status = (exists $expecting_map {$_})
		    ? $RESULT_UNEXPECTED_CORRECT_ERROR : $RESULT_CORRECT_ERROR;
	} elsif ($exit_value == 1) {
		$status = (exists $expecting_map {$_} and $expecting_map {$_} == $EXPECTING_WRONG_ERROR) 
		    ? $RESULT_EXPECTED_INCORRECT_ERROR : $RESULT_UNEXPECTED_INCORRECT_ERROR;
	} elsif ($exit_value == 2) {
		$status = (exists $expecting_map {$_} and $expecting_map {$_} == $EXPECTING_NO_ERROR)
		    ? $RESULT_EXPECTED_NO_ERROR : $RESULT_UNEXPECTED_NO_ERROR;
        } else {
	        $status = (exists $expecting_map {$_} and $expecting_map {$_} == $EXPECTING_WRONG_ERROR) 
		    ? $RESULT_EXPECTED_INCORRECT_ERROR : $RESULT_UNEXPECTED_CRASH;
        }

	$textmsg = "FAIL";

	if ($status == $RESULT_EXPECTED_NO_ERROR ||
	    $status == $RESULT_UNEXPECTED_CORRECT_ERROR ||
	    $status == $RESULT_EXPECTED_INCORRECT_ERROR ||
	    $status == $RESULT_CORRECT_ERROR){
	    $textmsg = "PASS";
	}
	push @{$status_items [$status]}, $_;
	print PROFILELOG "$textmsg: $tname $statuses[$status]\n"; print "$textmsg: $tname $statuses[$status]\n";
	$results_map{$_} = $status;
}
print "\n";
my $correct = scalar @{$status_items [$RESULT_CORRECT_ERROR]};
my $pct = sprintf("%.2f",($correct / $total) * 100);
print PROFILELOG $correct, " correctly detected errors ($pct %) \n\n";
print $correct, " correctly detected errors ($pct %) \n\n";

if (scalar @{$status_items [$RESULT_UNEXPECTED_CRASH]} > 0) {
    print PROFILELOG scalar @{$status_items [$RESULT_UNEXPECTED_CRASH]}, " compiler crashes\n";
    print scalar @{$status_items [$RESULT_UNEXPECTED_CRASH]}, " compiler crashes\n";
    print PROFILELOG "$_\n" foreach @{$status_items [$RESULT_UNEXPECTED_CRASH]};
    print "$_\n" foreach @{$status_items [$RESULT_UNEXPECTED_CRASH]};
}

if (scalar @{$status_items [$RESULT_UNEXPECTED_CORRECT_ERROR]} > 0) {
    print PROFILELOG scalar @{$status_items [$RESULT_UNEXPECTED_CORRECT_ERROR]}, " fixed error report(s), remove it from expect-wrong-error or expect-no-error !\n";
    print scalar @{$status_items [$RESULT_UNEXPECTED_CORRECT_ERROR]}, " fixed error report(s), remove it from expect-wrong-error or expect-no-error !\n";
    print PROFILELOG "$_\n" foreach @{$status_items [$RESULT_UNEXPECTED_CORRECT_ERROR]};
    print "$_\n" foreach @{$status_items [$RESULT_UNEXPECTED_CORRECT_ERROR]};
}

if (scalar @{$status_items [$RESULT_UNEXPECTED_INCORRECT_ERROR]} > 0) {
    print PROFILELOG scalar @{$status_items [$RESULT_UNEXPECTED_INCORRECT_ERROR]}, " new incorrect error report(s) !\n";
    print scalar @{$status_items [$RESULT_UNEXPECTED_INCORRECT_ERROR]}, " new incorrect error report(s) !\n";
    print PROFILELOG "$_\n" foreach @{$status_items [$RESULT_UNEXPECTED_INCORRECT_ERROR]};
    print "$_\n" foreach @{$status_items [$RESULT_UNEXPECTED_INCORRECT_ERROR]};
}

if (scalar @{$status_items [$RESULT_UNEXPECTED_NO_ERROR]} > 0) {
    print PROFILELOG scalar @{$status_items [$RESULT_UNEXPECTED_NO_ERROR]}, " new missing error report(s) !\n";
    print scalar @{$status_items [$RESULT_UNEXPECTED_NO_ERROR]}, " new missing error report(s) !\n";
    print PROFILELOG "$_\n" foreach @{$status_items [$RESULT_UNEXPECTED_NO_ERROR]};
    print "$_\n" foreach @{$status_items [$RESULT_UNEXPECTED_NO_ERROR]};
}

exit ((
	scalar @{$status_items [$RESULT_UNEXPECTED_INCORRECT_ERROR]} +
	scalar @{$status_items [$RESULT_UNEXPECTED_NO_ERROR       ]} +
	scalar @{$status_items [$RESULT_UNEXPECTED_CRASH          ]}
) == 0 ? 0 : 1);
