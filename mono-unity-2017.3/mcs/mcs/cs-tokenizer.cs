//
// cs-tokenizer.cs: The Tokenizer for the C# compiler
//                  This also implements the preprocessor
//
// Author: Miguel de Icaza (miguel@gnu.org)
//         Marek Safar (marek.safar@seznam.cz)
//
// Dual licensed under the terms of the MIT X11 or GNU GPL
//
// Copyright 2001, 2002 Ximian, Inc (http://www.ximian.com)
// Copyright 2004-2008 Novell, Inc
//
//

using System;
using System.Text;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Reflection;

namespace Mono.CSharp
{
	/// <summary>
	///    Tokenizer for C# source code. 
	/// </summary>

	public class Tokenizer : yyParser.yyInput
	{
		class KeywordEntry
		{
			public readonly int Token;
			public KeywordEntry Next;
			public readonly char[] Value;

			public KeywordEntry (string value, int token)
			{
				this.Value = value.ToCharArray ();
				this.Token = token;
			}
		}

		SeekableStreamReader reader;
		SourceFile ref_name;
		CompilationUnit file_name;
		CompilerContext context;
		bool hidden = false;
		int ref_line = 1;
		int line = 1;
		int col = 0;
		int previous_col;
		int current_token;
		bool handle_get_set = false;
		bool handle_remove_add = false;
		bool handle_where = false;
		bool handle_typeof = false;
		bool lambda_arguments_parsing;
		Location current_comment_location = Location.Null;
		ArrayList escaped_identifiers;
		int parsing_generic_less_than;
		
		//
		// Used mainly for parser optimizations. Some expressions for instance
		// can appear only in block (including initializer, base initializer)
		// scope only
		//
		public int parsing_block;
		internal bool query_parsing;
		
		// 
		// When parsing type only, useful for ambiguous nullable types
		//
		public int parsing_type;
		
		//
		// Set when parsing generic declaration (type or method header)
		//
		public bool parsing_generic_declaration;
		
		//
		// The value indicates that we have not reach any declaration or
		// namespace yet
		//
		public int parsing_declaration;

		//
		// The special character to inject on streams to trigger the EXPRESSION_PARSE
		// token to be returned.   It just happens to be a Unicode character that
		// would never be part of a program (can not be an identifier).
		//
		// This character is only tested just before the tokenizer is about to report
		// an error;   So on the regular operation mode, this addition will have no
		// impact on the tokenizer's performance.
		//
		
		public const int EvalStatementParserCharacter = 0x2190;   // Unicode Left Arrow
		public const int EvalCompilationUnitParserCharacter = 0x2191;  // Unicode Arrow
		public const int EvalUsingDeclarationsParserCharacter = 0x2192;  // Unicode Arrow
		
		//
		// XML documentation buffer. The save point is used to divide
		// comments on types and comments on members.
		//
		StringBuilder xml_comment_buffer;

		//
		// See comment on XmlCommentState enumeration.
		//
		XmlCommentState xml_doc_state = XmlCommentState.Allowed;

		//
		// Whether tokens have been seen on this line
		//
		bool tokens_seen = false;

		//
		// Set to true once the GENERATE_COMPLETION token has bee
		// returned.   This helps produce one GENERATE_COMPLETION,
		// as many COMPLETE_COMPLETION as necessary to complete the
		// AST tree and one final EOF.
		//
		bool generated;
		
		//
		// Whether a token has been seen on the file
		// This is needed because `define' is not allowed to be used
		// after a token has been seen.
		//
		bool any_token_seen = false;

		static readonly char[] simple_whitespaces = new char[] { ' ', '\t' };

		public bool PropertyParsing {
			get { return handle_get_set; }
			set { handle_get_set = value; }
		}

		public bool EventParsing {
			get { return handle_remove_add; }
			set { handle_remove_add = value; }
		}

		public bool ConstraintsParsing {
			get { return handle_where; }
			set { handle_where = value; }
		}

		public bool TypeOfParsing {
			get { return handle_typeof; }
			set { handle_typeof = value; }
		}
		
		public XmlCommentState doc_state {
			get { return xml_doc_state; }
			set {
				if (value == XmlCommentState.Allowed) {
					check_incorrect_doc_comment ();
					reset_doc_comment ();
				}
				xml_doc_state = value;
			}
		}

		//
		// This is used to trigger completion generation on the parser
		public bool CompleteOnEOF;
		
		void AddEscapedIdentifier (LocatedToken lt)
		{
			if (escaped_identifiers == null)
				escaped_identifiers = new ArrayList ();

			escaped_identifiers.Add (lt);
		}

		public bool IsEscapedIdentifier (Location loc)
		{
			if (escaped_identifiers != null) {
				foreach (LocatedToken lt in escaped_identifiers)
					if (lt.Location.Equals (loc))
						return true;
			}

			return false;
		}

		//
		// Class variables
		// 
		static KeywordEntry[][] keywords;
		static Hashtable keyword_strings;
		static NumberStyles styles;
		static NumberFormatInfo csharp_format_info;
		
		//
		// Values for the associated token returned
		//
		internal int putback_char; 	// Used by repl only
		Object val;

		//
		// Pre-processor
		//
		const int TAKING        = 1;
		const int ELSE_SEEN     = 4;
		const int PARENT_TAKING = 8;
		const int REGION        = 16;		

		//
		// pre-processor if stack state:
		//
		Stack ifstack;

		static System.Text.StringBuilder string_builder;

		const int max_id_size = 512;
		static char [] id_builder = new char [max_id_size];

		static CharArrayHashtable [] identifiers = new CharArrayHashtable [max_id_size + 1];

		const int max_number_size = 512;
		static char [] number_builder = new char [max_number_size];
		static int number_pos;
		
		//
		// Details about the error encoutered by the tokenizer
		//
		string error_details;
		
		public string error {
			get {
				return error_details;
			}
		}
		
		public int Line {
			get {
				return ref_line;
			}
		}

		//
		// This is used when the tokenizer needs to save
		// the current position as it needs to do some parsing
		// on its own to deamiguate a token in behalf of the
		// parser.
		//
		Stack position_stack = new Stack (2);
		class Position {
			public int position;
			public int line;
			public int ref_line;
			public int col;
			public bool hidden;
			public int putback_char;
			public int previous_col;
			public Stack ifstack;
			public int parsing_generic_less_than;
			public int current_token;

			public Position (Tokenizer t)
			{
				position = t.reader.Position;
				line = t.line;
				ref_line = t.ref_line;
				col = t.col;
				hidden = t.hidden;
				putback_char = t.putback_char;
				previous_col = t.previous_col;
				if (t.ifstack != null && t.ifstack.Count != 0)
					ifstack = (Stack)t.ifstack.Clone ();
				parsing_generic_less_than = t.parsing_generic_less_than;
				current_token = t.current_token;
			}
		}
		
		public void PushPosition ()
		{
			position_stack.Push (new Position (this));
		}

		public void PopPosition ()
		{
			Position p = (Position) position_stack.Pop ();

			reader.Position = p.position;
			ref_line = p.ref_line;
			line = p.line;
			col = p.col;
			hidden = p.hidden;
			putback_char = p.putback_char;
			previous_col = p.previous_col;
			ifstack = p.ifstack;
			parsing_generic_less_than = p.parsing_generic_less_than;
			current_token = p.current_token;
		}

		// Do not reset the position, ignore it.
		public void DiscardPosition ()
		{
			position_stack.Pop ();
		}
		
		static void AddKeyword (string kw, int token)
		{
			keyword_strings.Add (kw, kw);

			int length = kw.Length;
			if (keywords [length] == null) {
				keywords [length] = new KeywordEntry ['z' - '_' + 1];
			}

			int char_index = kw [0] - '_';
			KeywordEntry kwe = keywords [length] [char_index];
			if (kwe == null) {
				keywords [length] [char_index] = new KeywordEntry (kw, token);
				return;
			}

			while (kwe.Next != null) {
				kwe = kwe.Next;
			}

			kwe.Next = new KeywordEntry (kw, token);
		}

		static void InitTokens ()
		{
			keyword_strings = new Hashtable ();

			// 11 is the length of the longest keyword for now
			keywords = new KeywordEntry [11] [];

			AddKeyword ("__arglist", Token.ARGLIST);
			AddKeyword ("abstract", Token.ABSTRACT);
			AddKeyword ("as", Token.AS);
			AddKeyword ("add", Token.ADD);
			AddKeyword ("base", Token.BASE);
			AddKeyword ("bool", Token.BOOL);
			AddKeyword ("break", Token.BREAK);
			AddKeyword ("byte", Token.BYTE);
			AddKeyword ("case", Token.CASE);
			AddKeyword ("catch", Token.CATCH);
			AddKeyword ("char", Token.CHAR);
			AddKeyword ("checked", Token.CHECKED);
			AddKeyword ("class", Token.CLASS);
			AddKeyword ("const", Token.CONST);
			AddKeyword ("continue", Token.CONTINUE);
			AddKeyword ("decimal", Token.DECIMAL);
			AddKeyword ("default", Token.DEFAULT);
			AddKeyword ("delegate", Token.DELEGATE);
			AddKeyword ("do", Token.DO);
			AddKeyword ("double", Token.DOUBLE);
			AddKeyword ("else", Token.ELSE);
			AddKeyword ("enum", Token.ENUM);
			AddKeyword ("event", Token.EVENT);
			AddKeyword ("explicit", Token.EXPLICIT);
			AddKeyword ("extern", Token.EXTERN);
			AddKeyword ("false", Token.FALSE);
			AddKeyword ("finally", Token.FINALLY);
			AddKeyword ("fixed", Token.FIXED);
			AddKeyword ("float", Token.FLOAT);
			AddKeyword ("for", Token.FOR);
			AddKeyword ("foreach", Token.FOREACH);
			AddKeyword ("goto", Token.GOTO);
			AddKeyword ("get", Token.GET);
			AddKeyword ("if", Token.IF);
			AddKeyword ("implicit", Token.IMPLICIT);
			AddKeyword ("in", Token.IN);
			AddKeyword ("int", Token.INT);
			AddKeyword ("interface", Token.INTERFACE);
			AddKeyword ("internal", Token.INTERNAL);
			AddKeyword ("is", Token.IS);
			AddKeyword ("lock", Token.LOCK);
			AddKeyword ("long", Token.LONG);
			AddKeyword ("namespace", Token.NAMESPACE);
			AddKeyword ("new", Token.NEW);
			AddKeyword ("null", Token.NULL);
			AddKeyword ("object", Token.OBJECT);
			AddKeyword ("operator", Token.OPERATOR);
			AddKeyword ("out", Token.OUT);
			AddKeyword ("override", Token.OVERRIDE);
			AddKeyword ("params", Token.PARAMS);
			AddKeyword ("private", Token.PRIVATE);
			AddKeyword ("protected", Token.PROTECTED);
			AddKeyword ("public", Token.PUBLIC);
			AddKeyword ("readonly", Token.READONLY);
			AddKeyword ("ref", Token.REF);
			AddKeyword ("remove", Token.REMOVE);
			AddKeyword ("return", Token.RETURN);
			AddKeyword ("sbyte", Token.SBYTE);
			AddKeyword ("sealed", Token.SEALED);
			AddKeyword ("set", Token.SET);
			AddKeyword ("short", Token.SHORT);
			AddKeyword ("sizeof", Token.SIZEOF);
			AddKeyword ("stackalloc", Token.STACKALLOC);
			AddKeyword ("static", Token.STATIC);
			AddKeyword ("string", Token.STRING);
			AddKeyword ("struct", Token.STRUCT);
			AddKeyword ("switch", Token.SWITCH);
			AddKeyword ("this", Token.THIS);
			AddKeyword ("throw", Token.THROW);
			AddKeyword ("true", Token.TRUE);
			AddKeyword ("try", Token.TRY);
			AddKeyword ("typeof", Token.TYPEOF);
			AddKeyword ("uint", Token.UINT);
			AddKeyword ("ulong", Token.ULONG);
			AddKeyword ("unchecked", Token.UNCHECKED);
			AddKeyword ("unsafe", Token.UNSAFE);
			AddKeyword ("ushort", Token.USHORT);
			AddKeyword ("using", Token.USING);
			AddKeyword ("virtual", Token.VIRTUAL);
			AddKeyword ("void", Token.VOID);
			AddKeyword ("volatile", Token.VOLATILE);
			AddKeyword ("while", Token.WHILE);
			AddKeyword ("partial", Token.PARTIAL);
			AddKeyword ("where", Token.WHERE);

			// LINQ keywords
			AddKeyword ("from", Token.FROM);
			AddKeyword ("join", Token.JOIN);
			AddKeyword ("on", Token.ON);
			AddKeyword ("equals", Token.EQUALS);
			AddKeyword ("select", Token.SELECT);
			AddKeyword ("group", Token.GROUP);
			AddKeyword ("by", Token.BY);
			AddKeyword ("let", Token.LET);
			AddKeyword ("orderby", Token.ORDERBY);
			AddKeyword ("ascending", Token.ASCENDING);
			AddKeyword ("descending", Token.DESCENDING);
			AddKeyword ("into", Token.INTO);
		}

		//
		// Class initializer
		// 
		static Tokenizer ()
		{
			InitTokens ();			
			csharp_format_info = NumberFormatInfo.InvariantInfo;
			styles = NumberStyles.Float;

			string_builder = new System.Text.StringBuilder ();
		}

		int GetKeyword (char[] id, int id_len)
		{
			//
			// Keywords are stored in an array of arrays grouped by their
			// length and then by the first character
			//
			if (id_len >= keywords.Length || keywords [id_len] == null)
				return -1;

			int first_index = id [0] - '_';
			if (first_index > 'z')
				return -1;

			KeywordEntry kwe = keywords [id_len] [first_index];
			if (kwe == null)
				return -1;

			int res;
			do {
				res = kwe.Token;
				for (int i = 1; i < id_len; ++i) {
					if (id [i] != kwe.Value [i]) {
						res = 0;
						break;
					}
				}
				kwe = kwe.Next;
			} while (kwe != null && res == 0);

			if (res == 0)
				return -1;

			int next_token;
			switch (res) {
			case Token.GET:
			case Token.SET:
				if (!handle_get_set)
					res = -1;
				break;
			case Token.REMOVE:
			case Token.ADD:
				if (!handle_remove_add)
					res = -1;
				break;
			case Token.EXTERN:
				if (parsing_declaration == 0)
					res = Token.EXTERN_ALIAS;
				break;
			case Token.DEFAULT:
				if (peek_token () == Token.COLON) {
					token ();
					res = Token.DEFAULT_COLON;
				}
				break;
			case Token.WHERE:
				if (!handle_where && !query_parsing)
					res = -1;
				break;
			case Token.FROM:
				//
				// A query expression is any expression that starts with `from identifier'
				// followed by any token except ; , =
				// 
				if (!query_parsing) {
					if (lambda_arguments_parsing) {
						res = -1;
						break;
					}

					PushPosition ();
					// HACK: to disable generics micro-parser, because PushPosition does not
					// store identifiers array
					parsing_generic_less_than = 1;
					switch (xtoken ()) {
					case Token.IDENTIFIER:
					case Token.INT:
					case Token.BOOL:
					case Token.BYTE:
					case Token.CHAR:
					case Token.DECIMAL:
					case Token.FLOAT:
					case Token.LONG:
					case Token.OBJECT:
					case Token.STRING:
					case Token.UINT:
					case Token.ULONG:
						next_token = xtoken ();
						if (next_token == Token.SEMICOLON || next_token == Token.COMMA || next_token == Token.EQUALS)
							goto default;
						
						res = Token.FROM_FIRST;
						query_parsing = true;
						if (RootContext.Version <= LanguageVersion.ISO_2)
							Report.FeatureIsNotAvailable (Location, "query expressions");
						break;
					case Token.VOID:
						Expression.Error_VoidInvalidInTheContext (Location, Report);
						break;
					default:
						PopPosition ();
						// HACK: A token is not a keyword so we need to restore identifiers buffer
						// which has been overwritten before we grabbed the identifier
						id_builder [0] = 'f'; id_builder [1] = 'r'; id_builder [2] = 'o'; id_builder [3] = 'm';
						return -1;
					}
					PopPosition ();
				}
				break;
			case Token.JOIN:
			case Token.ON:
			case Token.EQUALS:
			case Token.SELECT:
			case Token.GROUP:
			case Token.BY:
			case Token.LET:
			case Token.ORDERBY:
			case Token.ASCENDING:
			case Token.DESCENDING:
			case Token.INTO:
				if (!query_parsing)
					res = -1;
				break;
				
			case Token.USING:
			case Token.NAMESPACE:
				// TODO: some explanation needed
				check_incorrect_doc_comment ();
				break;
				
			case Token.PARTIAL:
				if (parsing_block > 0) {
					res = -1;
					break;
				}

				// Save current position and parse next token.
				PushPosition ();

				next_token = token ();
				bool ok = (next_token == Token.CLASS) ||
					(next_token == Token.STRUCT) ||
					(next_token == Token.INTERFACE) ||
					(next_token == Token.VOID);

				PopPosition ();

				if (ok) {
					if (next_token == Token.VOID) {
						if (RootContext.Version == LanguageVersion.ISO_1 ||
						    RootContext.Version == LanguageVersion.ISO_2)
							Report.FeatureIsNotAvailable (Location, "partial methods");
					} else if (RootContext.Version == LanguageVersion.ISO_1)
						Report.FeatureIsNotAvailable (Location, "partial types");

					return res;
				}

				if (next_token < Token.LAST_KEYWORD) {
					Report.Error (267, Location,
						"The `partial' modifier can be used only immediately before `class', `struct', `interface', or `void' keyword");
					return token ();
				}					

				res = -1;
				break;
			}

			return res;
		}

		public Location Location {
			get {
				return new Location (ref_line, hidden ? -1 : col);
			}
		}

		public Tokenizer (SeekableStreamReader input, CompilationUnit file, CompilerContext ctx)
		{
			this.ref_name = file;
			this.file_name = file;
			this.context = ctx;
			reader = input;
			
			putback_char = -1;

			xml_comment_buffer = new StringBuilder ();

			//
			// FIXME: This could be `Location.Push' but we have to
			// find out why the MS compiler allows this
			//
			Mono.CSharp.Location.Push (file, file);
		}

		static bool is_identifier_start_character (int c)
		{
			return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || Char.IsLetter ((char)c);
		}

		static bool is_identifier_part_character (char c)
		{
			if (c >= 'a' && c <= 'z')
				return true;

			if (c >= 'A' && c <= 'Z')
				return true;

			if (c == '_' || (c >= '0' && c <= '9'))
				return true;

			if (c < 0x80)
				return false;

			return Char.IsLetter (c) || Char.GetUnicodeCategory (c) == UnicodeCategory.ConnectorPunctuation;
		}

		public static bool IsKeyword (string s)
		{
			return keyword_strings [s] != null;
		}

		//
		// Open parens micro parser. Detects both lambda and cast ambiguity.
		//
		
		int TokenizeOpenParens ()
		{
			int ptoken;
			current_token = -1;

			int bracket_level = 0;
			bool is_type = false;
			bool can_be_type = false;
			
			while (true) {
				ptoken = current_token;
				token ();

				switch (current_token) {
				case Token.CLOSE_PARENS:
					token ();
					
					//
					// Expression inside parens is lambda, (int i) => 
					//
					if (current_token == Token.ARROW) {
						if (RootContext.Version <= LanguageVersion.ISO_2)
							Report.FeatureIsNotAvailable (Location, "lambda expressions");

						return Token.OPEN_PARENS_LAMBDA;
					}

					//
					// Expression inside parens is single type, (int[])
					//
					if (is_type)
						return Token.OPEN_PARENS_CAST;

					//
					// Expression is possible cast, look at next token, (T)null
					//
					if (can_be_type) {
						switch (current_token) {
						case Token.OPEN_PARENS:
						case Token.BANG:
						case Token.TILDE:
						case Token.IDENTIFIER:
						case Token.LITERAL_INTEGER:
						case Token.LITERAL_FLOAT:
						case Token.LITERAL_DOUBLE:
						case Token.LITERAL_DECIMAL:
						case Token.LITERAL_CHARACTER:
						case Token.LITERAL_STRING:
						case Token.BASE:
						case Token.CHECKED:
						case Token.DELEGATE:
						case Token.FALSE:
						case Token.FIXED:
						case Token.NEW:
						case Token.NULL:
						case Token.SIZEOF:
						case Token.THIS:
						case Token.THROW:
						case Token.TRUE:
						case Token.TYPEOF:
						case Token.UNCHECKED:
						case Token.UNSAFE:
						case Token.DEFAULT:

						//
						// These can be part of a member access
						//
						case Token.INT:
						case Token.UINT:
						case Token.SHORT:
						case Token.USHORT:
						case Token.LONG:
						case Token.ULONG:
						case Token.DOUBLE:
						case Token.FLOAT:
						case Token.CHAR:
						case Token.BYTE:
						case Token.DECIMAL:
						case Token.BOOL:
							return Token.OPEN_PARENS_CAST;
						}
					}
					return Token.OPEN_PARENS;
					
				case Token.DOT:
				case Token.DOUBLE_COLON:
					if (ptoken != Token.IDENTIFIER && ptoken != Token.OP_GENERICS_GT)
						goto default;

					continue;

				case Token.IDENTIFIER:
					switch (ptoken) {
					case Token.DOT:
					case Token.OP_GENERICS_LT:
					case Token.COMMA:
					case Token.DOUBLE_COLON:
					case -1:
						if (bracket_level == 0)
							can_be_type = true;
						continue;
					default:
						can_be_type = is_type = false;
						continue;
					}

				case Token.OBJECT:
				case Token.STRING:
				case Token.BOOL:
				case Token.DECIMAL:
				case Token.FLOAT:
				case Token.DOUBLE:
				case Token.SBYTE:
				case Token.BYTE:
				case Token.SHORT:
				case Token.USHORT:
				case Token.INT:
				case Token.UINT:
				case Token.LONG:
				case Token.ULONG:
				case Token.CHAR:
				case Token.VOID:
					if (bracket_level == 0)
						is_type = true;
					continue;

				case Token.COMMA:
					if (bracket_level == 0) {
						bracket_level = 100;
						can_be_type = is_type = false;
					}
					continue;

				case Token.OP_GENERICS_LT:
				case Token.OPEN_BRACKET:
					if (bracket_level++ == 0)
						is_type = true;
					continue;

				case Token.OP_GENERICS_GT:
				case Token.CLOSE_BRACKET:
					--bracket_level;
					continue;

				case Token.INTERR_NULLABLE:
				case Token.STAR:
					if (bracket_level == 0)
						is_type = true;
					continue;

				case Token.REF:
				case Token.OUT:
					can_be_type = is_type = false;
					continue;

				default:
					return Token.OPEN_PARENS;
				}
			}
		}

		public static bool IsValidIdentifier (string s)
		{
			if (s == null || s.Length == 0)
				return false;

			if (!is_identifier_start_character (s [0]))
				return false;
			
			for (int i = 1; i < s.Length; i ++)
				if (! is_identifier_part_character (s [i]))
					return false;
			
			return true;
		}

		bool parse_less_than ()
		{
		start:
			int the_token = token ();
			if (the_token == Token.OPEN_BRACKET) {
				do {
					the_token = token ();
				} while (the_token != Token.CLOSE_BRACKET);
				the_token = token ();
			} else if (the_token == Token.IN || the_token == Token.OUT) {
				the_token = token ();
			}
			switch (the_token) {
			case Token.IDENTIFIER:
			case Token.OBJECT:
			case Token.STRING:
			case Token.BOOL:
			case Token.DECIMAL:
			case Token.FLOAT:
			case Token.DOUBLE:
			case Token.SBYTE:
			case Token.BYTE:
			case Token.SHORT:
			case Token.USHORT:
			case Token.INT:
			case Token.UINT:
			case Token.LONG:
			case Token.ULONG:
			case Token.CHAR:
			case Token.VOID:
				break;
			case Token.OP_GENERICS_GT:
				return true;

			default:
				return false;
			}
		again:
			the_token = token ();

			if (the_token == Token.OP_GENERICS_GT)
				return true;
			else if (the_token == Token.COMMA || the_token == Token.DOT || the_token == Token.DOUBLE_COLON)
				goto start;
			else if (the_token == Token.INTERR_NULLABLE || the_token == Token.STAR)
				goto again;
			else if (the_token == Token.OP_GENERICS_LT) {
				if (!parse_less_than ())
					return false;
				goto again;
			} else if (the_token == Token.OPEN_BRACKET) {
			rank_specifiers:
				the_token = token ();
				if (the_token == Token.CLOSE_BRACKET)
					goto again;
				else if (the_token == Token.COMMA)
					goto rank_specifiers;
				return false;
			}

			return false;
		}

		bool parse_generic_dimension (out int dimension)
		{
			dimension = 1;

		again:
			int the_token = token ();
			if (the_token == Token.OP_GENERICS_GT)
				return true;
			else if (the_token == Token.COMMA) {
				dimension++;
				goto again;
			}

			return false;
		}
		
		public int peek_token ()
		{
			int the_token;

			PushPosition ();
			the_token = token ();
			PopPosition ();
			
			return the_token;
		}
					
		//
		// Tonizes `?' using custom disambiguous rules to return one
		// of following tokens: INTERR_NULLABLE, OP_COALESCING, INTERR
		//
		// Tricky expression look like:
		//
		// Foo ? a = x ? b : c;
		//
		int TokenizePossibleNullableType ()
		{
			if (parsing_block == 0 || parsing_type > 0)
				return Token.INTERR_NULLABLE;

			int d = peek_char ();
			if (d == '?') {
				get_char ();
				return Token.OP_COALESCING;
			}

			switch (current_token) {
			case Token.CLOSE_PARENS:
			case Token.TRUE:
			case Token.FALSE:
			case Token.NULL:
			case Token.LITERAL_INTEGER:
			case Token.LITERAL_STRING:
				return Token.INTERR;
			}

			if (d != ' ') {
				if (d == ',' || d == ';' || d == '>')
					return Token.INTERR_NULLABLE;
				if (d == '*' || (d >= '0' && d <= '9'))
					return Token.INTERR;
			}

			PushPosition ();
			current_token = Token.NONE;
			int next_token;
			switch (xtoken ()) {
			case Token.LITERAL_INTEGER:
			case Token.LITERAL_STRING:
			case Token.LITERAL_CHARACTER:
			case Token.LITERAL_DECIMAL:
			case Token.LITERAL_DOUBLE:
			case Token.LITERAL_FLOAT:
			case Token.TRUE:
			case Token.FALSE:
			case Token.NULL:
			case Token.THIS:
			case Token.NEW:
				next_token = Token.INTERR;
				break;
				
			case Token.SEMICOLON:
			case Token.COMMA:
			case Token.CLOSE_PARENS:
			case Token.OPEN_BRACKET:
			case Token.OP_GENERICS_GT:
				next_token = Token.INTERR_NULLABLE;
				break;
				
			default:
				next_token = -1;
				break;
			}

			if (next_token == -1) {
				switch (xtoken ()) {
				case Token.COMMA:
				case Token.SEMICOLON:
				case Token.OPEN_BRACE:
				case Token.CLOSE_PARENS:
				case Token.IN:
					next_token = Token.INTERR_NULLABLE;
					break;
					
				case Token.COLON:
					next_token = Token.INTERR;
					break;							
					
				default:
					int ntoken;
					int interrs = 1;
					int colons = 0;
					//
					// All shorcuts failed, do it hard way
					//
					while ((ntoken = xtoken ()) != Token.EOF) {
						if (ntoken == Token.SEMICOLON)
							break;
						
						if (ntoken == Token.COLON) {
							if (++colons == interrs)
								break;
							continue;
						}
						
						if (ntoken == Token.INTERR) {
							++interrs;
							continue;
						}
					}
					
					next_token = colons != interrs ? Token.INTERR_NULLABLE : Token.INTERR;
					break;
				}
			}
			
			PopPosition ();
			return next_token;
		}

		bool decimal_digits (int c)
		{
			int d;
			bool seen_digits = false;
			
			if (c != -1){
				if (number_pos == max_number_size)
					Error_NumericConstantTooLong ();
				number_builder [number_pos++] = (char) c;
			}
			
			//
			// We use peek_char2, because decimal_digits needs to do a 
			// 2-character look-ahead (5.ToString for example).
			//
			while ((d = peek_char2 ()) != -1){
				if (d >= '0' && d <= '9'){
					if (number_pos == max_number_size)
						Error_NumericConstantTooLong ();
					number_builder [number_pos++] = (char) d;
					get_char ();
					seen_digits = true;
				} else
					break;
			}
			
			return seen_digits;
		}

		static bool is_hex (int e)
		{
			return (e >= '0' && e <= '9') || (e >= 'A' && e <= 'F') || (e >= 'a' && e <= 'f');
		}

		static int real_type_suffix (int c)
		{
			int t;

			switch (c){
			case 'F': case 'f':
				t =  Token.LITERAL_FLOAT;
				break;
			case 'D': case 'd':
				t = Token.LITERAL_DOUBLE;
				break;
			case 'M': case 'm':
				 t= Token.LITERAL_DECIMAL;
				break;
			default:
				return Token.NONE;
			}
			return t;
		}

		int integer_type_suffix (ulong ul, int c)
		{
			bool is_unsigned = false;
			bool is_long = false;

			if (c != -1){
				bool scanning = true;
				do {
					switch (c){
					case 'U': case 'u':
						if (is_unsigned)
							scanning = false;
						is_unsigned = true;
						get_char ();
						break;

					case 'l':
						if (!is_unsigned){
							//
							// if we have not seen anything in between
							// report this error
							//
							Report.Warning (78, 4, Location, "The 'l' suffix is easily confused with the digit '1' (use 'L' for clarity)");
						}
						//
						// This goto statement causes the MS CLR 2.0 beta 1 csc to report an error, so
						// work around that.
						//
						//goto case 'L';
						if (is_long)
							scanning = false;
						is_long = true;
						get_char ();
						break;

					case 'L': 
						if (is_long)
							scanning = false;
						is_long = true;
						get_char ();
						break;
						
					default:
						scanning = false;
						break;
					}
					c = peek_char ();
				} while (scanning);
			}

			if (is_long && is_unsigned){
				val = ul;
				return Token.LITERAL_INTEGER;
			} else if (is_unsigned){
				// uint if possible, or ulong else.

				if ((ul & 0xffffffff00000000) == 0)
					val = (uint) ul;
				else
					val = ul;
			} else if (is_long){
				// long if possible, ulong otherwise
				if ((ul & 0x8000000000000000) != 0)
					val = ul;
				else
					val = (long) ul;
			} else {
				// int, uint, long or ulong in that order
				if ((ul & 0xffffffff00000000) == 0){
					uint ui = (uint) ul;
					
					if ((ui & 0x80000000) != 0)
						val = ui;
					else
						val = (int) ui;
				} else {
					if ((ul & 0x8000000000000000) != 0)
						val = ul;
					else
						val = (long) ul;
				}
			}
			return Token.LITERAL_INTEGER;
		}
				
		//
		// given `c' as the next char in the input decide whether
		// we need to convert to a special type, and then choose
		// the best representation for the integer
		//
		int adjust_int (int c)
		{
			try {
				if (number_pos > 9){
					ulong ul = (uint) (number_builder [0] - '0');

					for (int i = 1; i < number_pos; i++){
						ul = checked ((ul * 10) + ((uint)(number_builder [i] - '0')));
					}
					return integer_type_suffix (ul, c);
				} else {
					uint ui = (uint) (number_builder [0] - '0');

					for (int i = 1; i < number_pos; i++){
						ui = checked ((ui * 10) + ((uint)(number_builder [i] - '0')));
					}
					return integer_type_suffix (ui, c);
				}
			} catch (OverflowException) {
				error_details = "Integral constant is too large";
				Report.Error (1021, Location, error_details);
				val = 0ul;
				return Token.LITERAL_INTEGER;
			}
			catch (FormatException) {
				Report.Error (1013, Location, "Invalid number");
				val = 0ul;
				return Token.LITERAL_INTEGER;
			}
		}
		
		int adjust_real (int t)
		{
			string s = new String (number_builder, 0, number_pos);
			const string error_details = "Floating-point constant is outside the range of type `{0}'";

			switch (t){
			case Token.LITERAL_DECIMAL:
				try {
					val = System.Decimal.Parse (s, styles, csharp_format_info);
				} catch (OverflowException) {
					val = 0m;     
					Report.Error (594, Location, error_details, "decimal");
				}
				break;
			case Token.LITERAL_FLOAT:
				try {
					val = float.Parse (s, styles, csharp_format_info);
				} catch (OverflowException) {
					val = 0.0f;     
					Report.Error (594, Location, error_details, "float");
				}
				break;
				
			case Token.LITERAL_DOUBLE:
			case Token.NONE:
				t = Token.LITERAL_DOUBLE;
				try {
					val = System.Double.Parse (s, styles, csharp_format_info);
				} catch (OverflowException) {
					val = 0.0;     
					Report.Error (594, Location, error_details, "double");
				}
				break;
			}
			return t;
		}

		int handle_hex ()
		{
			int d;
			ulong ul;
			
			get_char ();
			while ((d = peek_char ()) != -1){
				if (is_hex (d)){
					number_builder [number_pos++] = (char) d;
					get_char ();
				} else
					break;
			}
			
			string s = new String (number_builder, 0, number_pos);
			try {
				if (number_pos <= 8)
					ul = System.UInt32.Parse (s, NumberStyles.HexNumber);
				else
					ul = System.UInt64.Parse (s, NumberStyles.HexNumber);
			} catch (OverflowException){
				error_details = "Integral constant is too large";
				Report.Error (1021, Location, error_details);
				val = 0ul;
				return Token.LITERAL_INTEGER;
			}
			catch (FormatException) {
				Report.Error (1013, Location, "Invalid number");
				val = 0ul;
				return Token.LITERAL_INTEGER;
			}
			
			return integer_type_suffix (ul, peek_char ());
		}

		//
		// Invoked if we know we have .digits or digits
		//
		int is_number (int c)
		{
			bool is_real = false;
			int type;

			number_pos = 0;

			if (c >= '0' && c <= '9'){
				if (c == '0'){
					int peek = peek_char ();

					if (peek == 'x' || peek == 'X')
						return handle_hex ();
				}
				decimal_digits (c);
				c = get_char ();
			}

			//
			// We need to handle the case of
			// "1.1" vs "1.string" (LITERAL_FLOAT vs NUMBER DOT IDENTIFIER)
			//
			if (c == '.'){
				if (decimal_digits ('.')){
					is_real = true;
					c = get_char ();
				} else {
					putback ('.');
					number_pos--;
					return adjust_int (-1);
				}
			}
			
			if (c == 'e' || c == 'E'){
				is_real = true;
				if (number_pos == max_number_size)
					Error_NumericConstantTooLong ();
				number_builder [number_pos++] = 'e';
				c = get_char ();
				
				if (c == '+'){
					if (number_pos == max_number_size)
						Error_NumericConstantTooLong ();
					number_builder [number_pos++] = '+';
					c = -1;
				} else if (c == '-') {
					if (number_pos == max_number_size)
						Error_NumericConstantTooLong ();
					number_builder [number_pos++] = '-';
					c = -1;
				} else {
					if (number_pos == max_number_size)
						Error_NumericConstantTooLong ();
					number_builder [number_pos++] = '+';
				}
					
				decimal_digits (c);
				c = get_char ();
			}

			type = real_type_suffix (c);
			if (type == Token.NONE && !is_real){
				putback (c);
				return adjust_int (c);
			} else 
				is_real = true;

			if (type == Token.NONE){
				putback (c);
			}
			
			if (is_real)
				return adjust_real (type);

			Console.WriteLine ("This should not be reached");
			throw new Exception ("Is Number should never reach this point");
		}

		//
		// Accepts exactly count (4 or 8) hex, no more no less
		//
		int getHex (int count, out int surrogate, out bool error)
		{
			int i;
			int total = 0;
			int c;
			int top = count != -1 ? count : 4;
			
			get_char ();
			error = false;
			surrogate = 0;
			for (i = 0; i < top; i++){
				c = get_char ();

				if (c >= '0' && c <= '9')
					c = (int) c - (int) '0';
				else if (c >= 'A' && c <= 'F')
					c = (int) c - (int) 'A' + 10;
				else if (c >= 'a' && c <= 'f')
					c = (int) c - (int) 'a' + 10;
				else {
					error = true;
					return 0;
				}
				
				total = (total * 16) + c;
				if (count == -1){
					int p = peek_char ();
					if (p == -1)
						break;
					if (!is_hex ((char)p))
						break;
				}
			}

			if (top == 8) {
				if (total > 0x0010FFFF) {
					error = true;
					return 0;
				}

				if (total >= 0x00010000) {
					surrogate = ((total - 0x00010000) % 0x0400 + 0xDC00);					
					total = ((total - 0x00010000) / 0x0400 + 0xD800);
				}
			}

			return total;
		}

		int escape (int c, out int surrogate)
		{
			bool error;
			int d;
			int v;

			d = peek_char ();
			if (c != '\\') {
				surrogate = 0;
				return c;
			}
			
			switch (d){
			case 'a':
				v = '\a'; break;
			case 'b':
				v = '\b'; break;
			case 'n':
				v = '\n'; break;
			case 't':
				v = '\t'; break;
			case 'v':
				v = '\v'; break;
			case 'r':
				v = '\r'; break;
			case '\\':
				v = '\\'; break;
			case 'f':
				v = '\f'; break;
			case '0':
				v = 0; break;
			case '"':
				v = '"'; break;
			case '\'':
				v = '\''; break;
			case 'x':
				v = getHex (-1, out surrogate, out error);
				if (error)
					goto default;
				return v;
			case 'u':
			case 'U':
				return EscapeUnicode (d, out surrogate);
			default:
				surrogate = 0;
				Report.Error (1009, Location, "Unrecognized escape sequence `\\{0}'", ((char)d).ToString ());
				return d;
			}

			get_char ();
			surrogate = 0;
			return v;
		}

		int EscapeUnicode (int ch, out int surrogate)
		{
			bool error;
			if (ch == 'U') {
				ch = getHex (8, out surrogate, out error);
			} else {
				ch = getHex (4, out surrogate, out error);
			}

			if (error)
				Report.Error (1009, Location, "Unrecognized escape sequence");

			return ch;
		}

		int get_char ()
		{
			int x;
			if (putback_char != -1) {
				x = putback_char;
				putback_char = -1;
			} else
				x = reader.Read ();
			if (x == '\n') {
				advance_line ();
			} else {
				col++;
			}
			return x;
		}

		void advance_line ()
		{
			line++;
			ref_line++;
			previous_col = col;
			col = 0;
		}

		int peek_char ()
		{
			if (putback_char == -1)
				putback_char = reader.Read ();
			return putback_char;
		}

		int peek_char2 ()
		{
			if (putback_char != -1)
				return putback_char;
			return reader.Peek ();
		}
		
		void putback (int c)
		{
			if (putback_char != -1){
				Console.WriteLine ("Col: " + col);
				Console.WriteLine ("Row: " + line);
				Console.WriteLine ("Name: " + ref_name.Name);
				Console.WriteLine ("Current [{0}] putting back [{1}]  ", putback_char, c);
				throw new Exception ("This should not happen putback on putback");
			}
			if (c == '\n' || col == 0) {
				// It won't happen though.
				line--;
				ref_line--;
				col = previous_col;
			}
			else
				col--;
			putback_char = c;
		}

		public bool advance ()
		{
			return peek_char () != -1 || CompleteOnEOF;
		}

		public Object Value {
			get {
				return val;
			}
		}

		public Object value ()
		{
			return val;
		}

		public int token ()
		{
			current_token = xtoken ();
			return current_token;
		}

		static StringBuilder static_cmd_arg = new System.Text.StringBuilder ();

		void get_cmd_arg (out string cmd, out string arg)
		{
			int c;
			
			tokens_seen = false;
			arg = "";

			// skip over white space
			do {
				c = get_char ();
			} while (c == '\r' || c == ' ' || c == '\t');

			static_cmd_arg.Length = 0;
			while (c != -1 && is_identifier_part_character ((char)c)) {
				static_cmd_arg.Append ((char)c);
				c = get_char ();
				if (c == '\\') {
					int peek = peek_char ();
					if (peek == 'U' || peek == 'u') {
						int surrogate;
						c = EscapeUnicode (c, out surrogate);
						if (surrogate != 0) {
							if (is_identifier_part_character ((char) c))
								static_cmd_arg.Append ((char) c);
							c = surrogate;
						}
					}
				}
			}

			cmd = static_cmd_arg.ToString ();

			// skip over white space
			while (c == '\r' || c == ' ' || c == '\t')
				c = get_char ();

			static_cmd_arg.Length = 0;
			int has_identifier_argument = 0;

			while (c != -1 && c != '\n' && c != '\r') {
				if (c == '\\' && has_identifier_argument >= 0) {
					if (has_identifier_argument != 0 || (cmd == "define" || cmd == "if" || cmd == "elif" || cmd == "undef")) {
						has_identifier_argument = 1;

						int peek = peek_char ();
						if (peek == 'U' || peek == 'u') {
							int surrogate;
							c = EscapeUnicode (c, out surrogate);
							if (surrogate != 0) {
								if (is_identifier_part_character ((char) c))
									static_cmd_arg.Append ((char) c);
								c = surrogate;
							}
						}
					} else {
						has_identifier_argument = -1;
					}
				}
				static_cmd_arg.Append ((char) c);
				c = get_char ();
			}

			if (static_cmd_arg.Length != 0)
				arg = static_cmd_arg.ToString ();
		}

		//
		// Handles the #line directive
		//
		bool PreProcessLine (string arg)
		{
			if (arg.Length == 0)
				return false;

			if (arg == "default"){
				ref_line = line;
				ref_name = file_name;
				hidden = false;
				Location.Push (file_name, ref_name);
				return true;
			} else if (arg == "hidden"){
				hidden = true;
				return true;
			}
			
			try {
				int pos;

				if ((pos = arg.IndexOf (' ')) != -1 && pos != 0){
					ref_line = System.Int32.Parse (arg.Substring (0, pos));
					pos++;
					
					char [] quotes = { '\"' };
					
					string name = arg.Substring (pos). Trim (quotes);
					ref_name = Location.LookupFile (file_name, name);
					file_name.AddFile (ref_name);
					hidden = false;
					Location.Push (file_name, ref_name);
				} else {
					ref_line = System.Int32.Parse (arg);
					hidden = false;
				}
			} catch {
				return false;
			}
			
			return true;
		}

		//
		// Handles #define and #undef
		//
		void PreProcessDefinition (bool is_define, string ident, bool caller_is_taking)
		{
			if (ident.Length == 0 || ident == "true" || ident == "false"){
				Report.Error (1001, Location, "Missing identifier to pre-processor directive");
				return;
			}

			if (ident.IndexOfAny (simple_whitespaces) != -1){
				Error_EndLineExpected ();
				return;
			}

			if (!is_identifier_start_character (ident [0]))
				Report.Error (1001, Location, "Identifier expected: {0}", ident);
			
			foreach (char c in ident.Substring (1)){
				if (!is_identifier_part_character (c)){
					Report.Error (1001, Location, "Identifier expected: {0}",  ident);
					return;
				}
			}

			if (!caller_is_taking)
				return;

			if (is_define) {
				//
				// #define ident
				//
				if (RootContext.IsConditionalDefined (ident))
					return;

				file_name.AddDefine (ident);
			} else {
				//
				// #undef ident
				//
				file_name.AddUndefine (ident);
			}
		}

		static byte read_hex (string arg, int pos, out bool error)
		{
			error = false;

			int total;
			char c = arg [pos];

			if ((c >= '0') && (c <= '9'))
				total = (int) c - (int) '0';
			else if ((c >= 'A') && (c <= 'F'))
				total = (int) c - (int) 'A' + 10;
			else if ((c >= 'a') && (c <= 'f'))
				total = (int) c - (int) 'a' + 10;
			else {
				error = true;
				return 0;
			}

			total *= 16;
			c = arg [pos+1];

			if ((c >= '0') && (c <= '9'))
				total += (int) c - (int) '0';
			else if ((c >= 'A') && (c <= 'F'))
				total += (int) c - (int) 'A' + 10;
			else if ((c >= 'a') && (c <= 'f'))
				total += (int) c - (int) 'a' + 10;
			else {
				error = true;
				return 0;
			}

			return (byte) total;
		}

		/// <summary>
		/// Handles #pragma checksum
		/// </summary>
		bool PreProcessPragmaChecksum (string arg)
		{
			if ((arg [0] != ' ') && (arg [0] != '\t'))
				return false;

			arg = arg.Trim (simple_whitespaces);
			if ((arg.Length < 2) || (arg [0] != '"'))
				return false;

			StringBuilder file_sb = new StringBuilder ();

			int pos = 1;
			char ch;
			while ((ch = arg [pos++]) != '"') {
				if (pos >= arg.Length)
					return false;

				if (ch == '\\') {
					if (pos+1 >= arg.Length)
						return false;
					ch = arg [pos++];
				}

				file_sb.Append (ch);
			}

			if ((pos+2 >= arg.Length) || ((arg [pos] != ' ') && (arg [pos] != '\t')))
				return false;

			arg = arg.Substring (pos).Trim (simple_whitespaces);
			if ((arg.Length < 42) || (arg [0] != '"') || (arg [1] != '{') ||
			    (arg [10] != '-') || (arg [15] != '-') || (arg [20] != '-') ||
			    (arg [25] != '-') || (arg [38] != '}') || (arg [39] != '"'))
				return false;

			bool error;
			byte[] guid_bytes = new byte [16];

			for (int i = 0; i < 4; i++) {
				guid_bytes [i] = read_hex (arg, 2+2*i, out error);
				if (error)
					return false;
			}
			for (int i = 0; i < 2; i++) {
				guid_bytes [i+4] = read_hex (arg, 11+2*i, out error);
				if (error)
					return false;
				guid_bytes [i+6] = read_hex (arg, 16+2*i, out error);
				if (error)
					return false;
				guid_bytes [i+8] = read_hex (arg, 21+2*i, out error);
				if (error)
					return false;
			}

			for (int i = 0; i < 6; i++) {
				guid_bytes [i+10] = read_hex (arg, 26+2*i, out error);
				if (error)
					return false;
			}

			arg = arg.Substring (40).Trim (simple_whitespaces);
			if ((arg.Length < 34) || (arg [0] != '"') || (arg [33] != '"'))
				return false;

			byte[] checksum_bytes = new byte [16];
			for (int i = 0; i < 16; i++) {
				checksum_bytes [i] = read_hex (arg, 1+2*i, out error);
				if (error)
					return false;
			}

			arg = arg.Substring (34).Trim (simple_whitespaces);
			if (arg.Length > 0)
				return false;

			SourceFile file = Location.LookupFile (file_name, file_sb.ToString ());
			file.SetChecksum (guid_bytes, checksum_bytes);
			ref_name.AutoGenerated = true;
			return true;
		}

		/// <summary>
		/// Handles #pragma directive
		/// </summary>
		void PreProcessPragma (string arg)
		{
			const string warning = "warning";
			const string w_disable = "warning disable";
			const string w_restore = "warning restore";
			const string checksum = "checksum";

			if (arg == w_disable) {
				Report.RegisterWarningRegion (Location).WarningDisable (Location.Row);
				return;
			}

			if (arg == w_restore) {
				Report.RegisterWarningRegion (Location).WarningEnable (Location.Row);
				return;
			}

			if (arg.StartsWith (w_disable)) {
				int[] codes = ParseNumbers (arg.Substring (w_disable.Length));
				foreach (int code in codes) {
					if (code != 0)
						Report.RegisterWarningRegion (Location).WarningDisable (Location, code, Report);
				}
				return;
			}

			if (arg.StartsWith (w_restore)) {
				int[] codes = ParseNumbers (arg.Substring (w_restore.Length));
				Hashtable w_table = Report.warning_ignore_table;
				foreach (int code in codes) {
					if (w_table != null && w_table.Contains (code))
						Report.Warning (1635, 1, Location, "Cannot restore warning `CS{0:0000}' because it was disabled globally", code);
					Report.RegisterWarningRegion (Location).WarningEnable (Location, code, Report);
				}
				return;
			}

			if (arg.StartsWith (warning)) {
				Report.Warning (1634, 1, Location, "Expected disable or restore");
				return;
			}

			if (arg.StartsWith (checksum)) {
				if (!PreProcessPragmaChecksum (arg.Substring (checksum.Length)))
					Warning_InvalidPragmaChecksum ();
				return;
			}

			Report.Warning (1633, 1, Location, "Unrecognized #pragma directive");
		}

		int[] ParseNumbers (string text)
		{
			string[] string_array = text.Split (',');
			int[] values = new int [string_array.Length];
			int index = 0;
			foreach (string string_code in string_array) {
				try {
					values[index++] = int.Parse (string_code, System.Globalization.CultureInfo.InvariantCulture);
				}
				catch (FormatException) {
					Report.Warning (1692, 1, Location, "Invalid number");
				}
			}
			return values;
		}

		bool eval_val (string s)
		{
			if (s == "true")
				return true;
			if (s == "false")
				return false;

			return file_name.IsConditionalDefined (s);
		}

		bool pp_primary (ref string s)
		{
			s = s.Trim ();
			int len = s.Length;

			if (len > 0){
				char c = s [0];
				
				if (c == '('){
					s = s.Substring (1);
					bool val = pp_expr (ref s, false);
					if (s.Length > 0 && s [0] == ')'){
						s = s.Substring (1);
						return val;
					}
					Error_InvalidDirective ();
					return false;
				}
				
				if (is_identifier_start_character (c)){
					int j = 1;

					while (j < len){
						c = s [j];
						
						if (is_identifier_part_character (c)){
							j++;
							continue;
						}
						bool v = eval_val (s.Substring (0, j));
						s = s.Substring (j);
						return v;
					}
					bool vv = eval_val (s);
					s = "";
					return vv;
				}
			}
			Error_InvalidDirective ();
			return false;
		}
		
		bool pp_unary (ref string s)
		{
			s = s.Trim ();
			int len = s.Length;

			if (len > 0){
				if (s [0] == '!'){
					if (len > 1 && s [1] == '='){
						Error_InvalidDirective ();
						return false;
					}
					s = s.Substring (1);
					return ! pp_primary (ref s);
				} else
					return pp_primary (ref s);
			} else {
				Error_InvalidDirective ();
				return false;
			}
		}
		
		bool pp_eq (ref string s)
		{
			bool va = pp_unary (ref s);

			s = s.Trim ();
			int len = s.Length;
			if (len > 0){
				if (s [0] == '='){
					if (len > 2 && s [1] == '='){
						s = s.Substring (2);
						return va == pp_unary (ref s);
					} else {
						Error_InvalidDirective ();
						return false;
					}
				} else if (s [0] == '!' && len > 1 && s [1] == '='){
					s = s.Substring (2);

					return va != pp_unary (ref s);

				} 
			}

			return va;
				
		}
		
		bool pp_and (ref string s)
		{
			bool va = pp_eq (ref s);

			s = s.Trim ();
			int len = s.Length;
			if (len > 0){
				if (s [0] == '&'){
					if (len > 2 && s [1] == '&'){
						s = s.Substring (2);
						return (va & pp_and (ref s));
					} else {
						Error_InvalidDirective ();
						return false;
					}
				} 
			}
			return va;
		}
		
		//
		// Evaluates an expression for `#if' or `#elif'
		//
		bool pp_expr (ref string s, bool isTerm)
		{
			bool va = pp_and (ref s);
			s = s.Trim ();
			int len = s.Length;
			if (len > 0){
				char c = s [0];
				
				if (c == '|'){
					if (len > 2 && s [1] == '|'){
						s = s.Substring (2);
						return va | pp_expr (ref s, isTerm);
					} else {
						Error_InvalidDirective ();
						return false;
					}
				}
				if (isTerm) {
					Error_EndLineExpected ();
					return false;
				}
			}
			
			return va;
		}

		bool eval (string s)
		{
			bool v = pp_expr (ref s, true);
			s = s.Trim ();
			if (s.Length != 0){
				return false;
			}

			return v;
		}

		void Error_NumericConstantTooLong ()
		{
			Report.Error (1021, Location, "Numeric constant too long");			
		}
		
		void Error_InvalidDirective ()
		{
			Report.Error (1517, Location, "Invalid preprocessor directive");
		}

		void Error_UnexpectedDirective (string extra)
		{
			Report.Error (
				1028, Location,
				"Unexpected processor directive ({0})", extra);
		}

		void Error_TokensSeen ()
		{
			Report.Error (1032, Location,
				"Cannot define or undefine preprocessor symbols after first token in file");
		}

		void Eror_WrongPreprocessorLocation ()
		{
			Report.Error (1040, Location,
				"Preprocessor directives must appear as the first non-whitespace character on a line");
		}

		void Error_EndLineExpected ()
		{
			Report.Error (1025, Location, "Single-line comment or end-of-line expected");
		}
		
		void Warning_InvalidPragmaChecksum ()
		{
			Report.Warning (1695, 1, Location,
					"Invalid #pragma checksum syntax; should be " +
					"#pragma checksum \"filename\" " +
					"\"{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}\" \"XXXX...\"");
		}
		//
		// if true, then the code continues processing the code
		// if false, the code stays in a loop until another directive is
		// reached.
		// When caller_is_taking is false we ignore all directives except the ones
		// which can help us to identify where the #if block ends
		bool handle_preprocessing_directive (bool caller_is_taking)
		{
			string cmd, arg;
			bool region_directive = false;

			get_cmd_arg (out cmd, out arg);

			// Eat any trailing whitespaces and single-line comments
			if (arg.IndexOf ("//") != -1)
				arg = arg.Substring (0, arg.IndexOf ("//"));
			arg = arg.Trim (simple_whitespaces);

			//
			// The first group of pre-processing instructions is always processed
			//
			switch (cmd){
			case "region":
				region_directive = true;
				arg = "true";
				goto case "if";

			case "endregion":
				if (ifstack == null || ifstack.Count == 0){
					Error_UnexpectedDirective ("no #region for this #endregion");
					return true;
				}
				int pop = (int) ifstack.Pop ();
					
				if ((pop & REGION) == 0)
					Report.Error (1027, Location, "Expected `#endif' directive");
					
				return caller_is_taking;
				
			case "if":
				if (ifstack == null)
					ifstack = new Stack (2);

				int flags = region_directive ? REGION : 0;
				if (ifstack.Count == 0){
					flags |= PARENT_TAKING;
				} else {
					int state = (int) ifstack.Peek ();
					if ((state & TAKING) != 0) {
						flags |= PARENT_TAKING;
					}
				}

				if (caller_is_taking && eval (arg)) {
					ifstack.Push (flags | TAKING);
					return true;
				}
				ifstack.Push (flags);
				return false;
				
			case "endif":
				if (ifstack == null || ifstack.Count == 0){
					Error_UnexpectedDirective ("no #if for this #endif");
					return true;
				} else {
					pop = (int) ifstack.Pop ();
					
					if ((pop & REGION) != 0)
						Report.Error (1038, Location, "#endregion directive expected");
					
					if (arg.Length != 0) {
						Error_EndLineExpected ();
					}
					
					if (ifstack.Count == 0)
						return true;

					int state = (int) ifstack.Peek ();
					return (state & TAKING) != 0;
				}

			case "elif":
				if (ifstack == null || ifstack.Count == 0){
					Error_UnexpectedDirective ("no #if for this #elif");
					return true;
				} else {
					int state = (int) ifstack.Pop ();

					if ((state & REGION) != 0) {
						Report.Error (1038, Location, "#endregion directive expected");
						return true;
					}

					if ((state & ELSE_SEEN) != 0){
						Error_UnexpectedDirective ("#elif not valid after #else");
						return true;
					}

					if ((state & TAKING) != 0) {
						ifstack.Push (0);
						return false;
					}

					if (eval (arg) && ((state & PARENT_TAKING) != 0)){
						ifstack.Push (state | TAKING);
						return true;
					}

					ifstack.Push (state);
					return false;
				}

			case "else":
				if (ifstack == null || ifstack.Count == 0){
					Error_UnexpectedDirective ("no #if for this #else");
					return true;
				} else {
					int state = (int) ifstack.Peek ();

					if ((state & REGION) != 0) {
						Report.Error (1038, Location, "#endregion directive expected");
						return true;
					}

					if ((state & ELSE_SEEN) != 0){
						Error_UnexpectedDirective ("#else within #else");
						return true;
					}

					ifstack.Pop ();

					if (arg.Length != 0) {
						Error_EndLineExpected ();
						return true;
					}

					bool ret = false;
					if ((state & PARENT_TAKING) != 0) {
						ret = (state & TAKING) == 0;
					
						if (ret)
							state |= TAKING;
						else
							state &= ~TAKING;
					}
	
					ifstack.Push (state | ELSE_SEEN);
					
					return ret;
				}
			case "define":
				if (any_token_seen){
					Error_TokensSeen ();
					return caller_is_taking;
				}
				PreProcessDefinition (true, arg, caller_is_taking);
				return caller_is_taking;

			case "undef":
				if (any_token_seen){
					Error_TokensSeen ();
					return caller_is_taking;
				}
				PreProcessDefinition (false, arg, caller_is_taking);
				return caller_is_taking;
			}

			//
			// These are only processed if we are in a `taking' block
			//
			if (!caller_is_taking)
				return false;
					
			switch (cmd){
			case "error":
				Report.Error (1029, Location, "#error: '{0}'", arg);
				return true;

			case "warning":
				Report.Warning (1030, 1, Location, "#warning: `{0}'", arg);
				return true;

			case "pragma":
				if (RootContext.Version == LanguageVersion.ISO_1) {
					Report.FeatureIsNotAvailable (Location, "#pragma");
					return true;
				}

				PreProcessPragma (arg);
				return true;

			case "line":
				if (!PreProcessLine (arg))
					Report.Error (
						1576, Location,
						"The line number specified for #line directive is missing or invalid");
				return caller_is_taking;
			}

			Report.Error (1024, Location, "Wrong preprocessor directive");
			return true;

		}

		private int consume_string (bool quoted)
		{
			int c;
			string_builder.Length = 0;

			while ((c = get_char ()) != -1){
				if (c == '"'){
					if (quoted && peek_char () == '"'){
						string_builder.Append ((char) c);
						get_char ();
						continue;
					} else {
						val = string_builder.ToString ();
						return Token.LITERAL_STRING;
					}
				}

				if (c == '\n'){
					if (!quoted)
						Report.Error (1010, Location, "Newline in constant");
				}

				if (!quoted){
					int surrogate;
					c = escape (c, out surrogate);
					if (c == -1)
						return Token.ERROR;
					if (surrogate != 0) {
						string_builder.Append ((char) c);
						c = surrogate;
					}
				}
				string_builder.Append ((char) c);
			}

			Report.Error (1039, Location, "Unterminated string literal");
			return Token.EOF;
		}

		private int consume_identifier (int s)
		{
			int res = consume_identifier (s, false);

			if (doc_state == XmlCommentState.Allowed)
				doc_state = XmlCommentState.NotAllowed;

			return res;
		}

		private int consume_identifier (int c, bool quoted) 
		{
			int pos = 0;

			if (c == '\\') {
				int surrogate;
				c = escape (c, out surrogate);
				if (surrogate != 0) {
					id_builder [pos++] = (char) c;
					c = surrogate;
				}
			}

			id_builder [pos++] = (char) c;
			Location loc = Location;

			while ((c = get_char ()) != -1) {
			loop:
				if (is_identifier_part_character ((char) c)){
					if (pos == max_id_size){
						Report.Error (645, loc, "Identifier too long (limit is 512 chars)");
						return Token.ERROR;
					}
					
					id_builder [pos++] = (char) c;
				} else if (c == '\\') {
					int surrogate;
					c = escape (c, out surrogate);
					if (surrogate != 0) {
						if (is_identifier_part_character ((char) c))
							id_builder [pos++] = (char) c;
						c = surrogate;
					}
					goto loop;
				} else {
					putback (c);
					break;
				}
			}

			//
			// Optimization: avoids doing the keyword lookup
			// on uppercase letters
			//
			if (id_builder [0] >= '_' && !quoted) {
				int keyword = GetKeyword (id_builder, pos);
				if (keyword != -1) {
					// TODO: No need to store location for keyword, required location cleanup
					val = loc;
					return keyword;
				}
			}

			//
			// Keep identifiers in an array of hashtables to avoid needless
			// allocations
			//
			CharArrayHashtable identifiers_group = identifiers [pos];
			if (identifiers_group != null) {
				val = identifiers_group [id_builder];
				if (val != null) {
					val = new LocatedToken (loc, (string) val);
					if (quoted)
						AddEscapedIdentifier ((LocatedToken) val);
					return Token.IDENTIFIER;
				}
			} else {
				identifiers_group = new CharArrayHashtable (pos);
				identifiers [pos] = identifiers_group;
			}

			char [] chars = new char [pos];
			Array.Copy (id_builder, chars, pos);

			val = new String (id_builder, 0, pos);
			identifiers_group.Add (chars, val);

			if (RootContext.Version == LanguageVersion.ISO_1) {
				for (int i = 1; i < chars.Length; i += 3) {
					if (chars [i] == '_' && (chars [i - 1] == '_' || chars [i + 1] == '_')) {
						Report.Error (1638, loc,
							"`{0}': Any identifier with double underscores cannot be used when ISO language version mode is specified", val.ToString ());
					}
				}
			}

			val = new LocatedToken (loc, (string) val);
			if (quoted)
				AddEscapedIdentifier ((LocatedToken) val);
			return Token.IDENTIFIER;
		}
		
		public int xtoken ()
		{
			int d, c;

			// Whether we have seen comments on the current line
			bool comments_seen = false;
			while ((c = get_char ()) != -1) {
				switch (c) {
				case '\t':
					col = ((col + 8) / 8) * 8;
					continue;

				case ' ':
				case '\f':
				case '\v':
				case 0xa0:
				case 0:
				case 0xFEFF:	// Ignore BOM anywhere in the file
					continue;

/*				This is required for compatibility with .NET
				case 0xEF:
					if (peek_char () == 0xBB) {
						PushPosition ();
						get_char ();
						if (get_char () == 0xBF)
							continue;
						PopPosition ();
					}
					break;
*/
				case '\r':
					if (peek_char () != '\n')
						advance_line ();
					else
						get_char ();

					any_token_seen |= tokens_seen;
					tokens_seen = false;
					comments_seen = false;
					continue;

				case '\\':
					tokens_seen = true;
					return consume_identifier (c);

				case '{':
					val = Location;
					return Token.OPEN_BRACE;
				case '}':
					val = Location;
					return Token.CLOSE_BRACE;
				case '[':
					// To block doccomment inside attribute declaration.
					if (doc_state == XmlCommentState.Allowed)
						doc_state = XmlCommentState.NotAllowed;
					return Token.OPEN_BRACKET;
				case ']':
					return Token.CLOSE_BRACKET;
				case '(':
					val = Location;
					//
					// An expression versions of parens can appear in block context only
					//
					if (parsing_block != 0 && !lambda_arguments_parsing) {
						
						//
						// Optmize most common case where we know that parens
						// is not special
						//
						switch (current_token) {
						case Token.IDENTIFIER:
						case Token.IF:
						case Token.FOR:
						case Token.FOREACH:
						case Token.TYPEOF:
						case Token.WHILE:
						case Token.USING:
						case Token.DEFAULT:
						case Token.DELEGATE:
						case Token.OP_GENERICS_GT:
							return Token.OPEN_PARENS;
						}

						// Optimize using peek
						int xx = peek_char ();
						switch (xx) {
						case '(':
						case '\'':
						case '"':
						case '0':
						case '1':
							return Token.OPEN_PARENS;
						}

						lambda_arguments_parsing = true;
						PushPosition ();
						d = TokenizeOpenParens ();
						PopPosition ();
						lambda_arguments_parsing = false;
						return d;
					}

					return Token.OPEN_PARENS;
				case ')':
					return Token.CLOSE_PARENS;
				case ',':
					return Token.COMMA;
				case ';':
					return Token.SEMICOLON;
				case '~':
					return Token.TILDE;
				case '?':
					return TokenizePossibleNullableType ();
				case '<':
					if (parsing_generic_less_than++ > 0)
						return Token.OP_GENERICS_LT;

					return TokenizeLessThan ();

				case '>':
					d = peek_char ();

					if (d == '='){
						get_char ();
						return Token.OP_GE;
					}

					if (parsing_generic_less_than > 1 || (parsing_generic_less_than == 1 && d != '>')) {
						parsing_generic_less_than--;
						return Token.OP_GENERICS_GT;
					}

					if (d == '>') {
						get_char ();
						d = peek_char ();

						if (d == '=') {
							get_char ();
							return Token.OP_SHIFT_RIGHT_ASSIGN;
						}
						return Token.OP_SHIFT_RIGHT;
					}

					return Token.OP_GT;

				case '+':
					d = peek_char ();
					if (d == '+') {
						d = Token.OP_INC;
					} else if (d == '=') {
						d = Token.OP_ADD_ASSIGN;
					} else {
						return Token.PLUS;
					}
					get_char ();
					return d;

				case '-':
					d = peek_char ();
					if (d == '-') {
						d = Token.OP_DEC;
					} else if (d == '=')
						d = Token.OP_SUB_ASSIGN;
					else if (d == '>')
						d = Token.OP_PTR;
					else {
						return Token.MINUS;
					}
					get_char ();
					return d;

				case '!':
					if (peek_char () == '='){
						get_char ();
						return Token.OP_NE;
					}
					return Token.BANG;

				case '=':
					d = peek_char ();
					if (d == '='){
						get_char ();
						return Token.OP_EQ;
					}
					if (d == '>'){
						get_char ();
						return Token.ARROW;
					}

					return Token.ASSIGN;

				case '&':
					d = peek_char ();
					if (d == '&'){
						get_char ();
						return Token.OP_AND;
					}
					if (d == '='){
						get_char ();
						return Token.OP_AND_ASSIGN;
					}
					return Token.BITWISE_AND;

				case '|':
					d = peek_char ();
					if (d == '|'){
						get_char ();
						return Token.OP_OR;
					}
					if (d == '='){
						get_char ();
						return Token.OP_OR_ASSIGN;
					}
					return Token.BITWISE_OR;

				case '*':
					if (peek_char () == '='){
						get_char ();
						return Token.OP_MULT_ASSIGN;
					}
					val = Location;
					return Token.STAR;

				case '/':
					d = peek_char ();
					if (d == '='){
						get_char ();
						return Token.OP_DIV_ASSIGN;
					}

					// Handle double-slash comments.
					if (d == '/'){
						get_char ();
						if (RootContext.Documentation != null && peek_char () == '/') {
							get_char ();
							// Don't allow ////.
							if ((d = peek_char ()) != '/') {
								update_comment_location ();
								if (doc_state == XmlCommentState.Allowed)
									handle_one_line_xml_comment ();
								else if (doc_state == XmlCommentState.NotAllowed)
									warn_incorrect_doc_comment ();
							}
						}
						while ((d = get_char ()) != -1 && (d != '\n') && d != '\r');

						any_token_seen |= tokens_seen;
						tokens_seen = false;
						comments_seen = false;
						continue;
					} else if (d == '*'){
						get_char ();
						bool docAppend = false;
						if (RootContext.Documentation != null && peek_char () == '*') {
							get_char ();
							update_comment_location ();
							// But when it is /**/, just do nothing.
							if (peek_char () == '/') {
								get_char ();
								continue;
							}
							if (doc_state == XmlCommentState.Allowed)
								docAppend = true;
							else if (doc_state == XmlCommentState.NotAllowed)
								warn_incorrect_doc_comment ();
						}

						int current_comment_start = 0;
						if (docAppend) {
							current_comment_start = xml_comment_buffer.Length;
							xml_comment_buffer.Append (Environment.NewLine);
						}

						while ((d = get_char ()) != -1){
							if (d == '*' && peek_char () == '/'){
								get_char ();
								comments_seen = true;
								break;
							}
							if (docAppend)
								xml_comment_buffer.Append ((char) d);
							
							if (d == '\n'){
								any_token_seen |= tokens_seen;
								tokens_seen = false;
								// 
								// Reset 'comments_seen' just to be consistent.
								// It doesn't matter either way, here.
								//
								comments_seen = false;
							}
						}
						if (!comments_seen)
							Report.Error (1035, Location, "End-of-file found, '*/' expected");

						if (docAppend)
							update_formatted_doc_comment (current_comment_start);
						continue;
					}
					return Token.DIV;

				case '%':
					if (peek_char () == '='){
						get_char ();
						return Token.OP_MOD_ASSIGN;
					}
					return Token.PERCENT;

				case '^':
					if (peek_char () == '='){
						get_char ();
						return Token.OP_XOR_ASSIGN;
					}
					return Token.CARRET;

				case ':':
					if (peek_char () == ':') {
						get_char ();
						return Token.DOUBLE_COLON;
					}
					return Token.COLON;

				case '0': case '1': case '2': case '3': case '4':
				case '5': case '6': case '7': case '8': case '9':
					tokens_seen = true;
					return is_number (c);

				case '\n': // white space
					any_token_seen |= tokens_seen;
					tokens_seen = false;
					comments_seen = false;
					continue;

				case '.':
					tokens_seen = true;
					d = peek_char ();
					if (d >= '0' && d <= '9')
						return is_number (c);
					return Token.DOT;
				
				case '#':
					if (tokens_seen || comments_seen) {
						Eror_WrongPreprocessorLocation ();
						return Token.ERROR;
					}
					
					if (handle_preprocessing_directive (true))
						continue;

					bool directive_expected = false;
					while ((c = get_char ()) != -1) {
						if (col == 1) {
							directive_expected = true;
						} else if (!directive_expected) {
							// TODO: Implement comment support for disabled code and uncomment this code
//							if (c == '#') {
//								Eror_WrongPreprocessorLocation ();
//								return Token.ERROR;
//							}
							continue;
						}

						if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v' )
							continue;

						if (c == '#') {
							if (handle_preprocessing_directive (false))
								break;
						}
						directive_expected = false;
					}

					if (c != -1) {
						tokens_seen = false;
						continue;
					}

					return Token.EOF;
				
				case '"':
					return consume_string (false);

				case '\'':
					return TokenizeBackslash ();
				
				case '@':
					c = get_char ();
					if (c == '"') {
						tokens_seen = true;
						return consume_string (true);
					}

					if (is_identifier_start_character (c)){
						return consume_identifier (c, true);
					}

					Report.Error (1646, Location, "Keyword, identifier, or string expected after verbatim specifier: @");
					return Token.ERROR;

				case EvalStatementParserCharacter:
					return Token.EVAL_STATEMENT_PARSER;
				case EvalCompilationUnitParserCharacter:
					return Token.EVAL_COMPILATION_UNIT_PARSER;
				case EvalUsingDeclarationsParserCharacter:
					return Token.EVAL_USING_DECLARATIONS_UNIT_PARSER;
				}

				if (is_identifier_start_character (c)) {
					tokens_seen = true;
					return consume_identifier (c);
				}

				error_details = ((char)c).ToString ();
				return Token.ERROR;
			}

			if (CompleteOnEOF){
				if (generated)
					return Token.COMPLETE_COMPLETION;
				
				generated = true;
				return Token.GENERATE_COMPLETION;
			}
			

			return Token.EOF;
		}

		int TokenizeBackslash ()
		{
			int c = get_char ();
			tokens_seen = true;
			if (c == '\'') {
				error_details = "Empty character literal";
				Report.Error (1011, Location, error_details);
				return Token.ERROR;
			}
			if (c == '\r' || c == '\n') {
				Report.Error (1010, Location, "Newline in constant");
				return Token.ERROR;
			}

			int d;
			c = escape (c, out d);
			if (c == -1)
				return Token.ERROR;
			if (d != 0)
				throw new NotImplementedException ();

			val = (char) c;
			c = get_char ();

			if (c != '\'') {
				Report.Error (1012, Location, "Too many characters in character literal");

				// Try to recover, read until newline or next "'"
				while ((c = get_char ()) != -1) {
					if (c == '\n' || c == '\'')
						break;
				}
				return Token.ERROR;
			}

			return Token.LITERAL_CHARACTER;
		}

		int TokenizeLessThan ()
		{
			int d;
			if (handle_typeof) {
				PushPosition ();
				if (parse_generic_dimension (out d)) {
					val = d;
					DiscardPosition ();
					return Token.GENERIC_DIMENSION;
				}
				PopPosition ();
			}

			// Save current position and parse next token.
			PushPosition ();
			if (parse_less_than ()) {
				if (parsing_generic_declaration && token () != Token.DOT) {
					d = Token.OP_GENERICS_LT_DECL;
				} else {
					d = Token.OP_GENERICS_LT;
				}
				PopPosition ();
				return d;
			}

			PopPosition ();
			parsing_generic_less_than = 0;

			d = peek_char ();
			if (d == '<') {
				get_char ();
				d = peek_char ();

				if (d == '=') {
					get_char ();
					return Token.OP_SHIFT_LEFT_ASSIGN;
				}
				return Token.OP_SHIFT_LEFT;
			}

			if (d == '=') {
				get_char ();
				return Token.OP_LE;
			}
			return Token.OP_LT;
		}

		//
		// Handles one line xml comment
		//
		private void handle_one_line_xml_comment ()
		{
			int c;
			while ((c = peek_char ()) == ' ')
				get_char (); // skip heading whitespaces.
			while ((c = peek_char ()) != -1 && c != '\n' && c != '\r') {
				xml_comment_buffer.Append ((char) get_char ());
			}
			if (c == '\r' || c == '\n')
				xml_comment_buffer.Append (Environment.NewLine);
		}

		//
		// Remove heading "*" in Javadoc-like xml documentation.
		//
		private void update_formatted_doc_comment (int current_comment_start)
		{
			int length = xml_comment_buffer.Length - current_comment_start;
			string [] lines = xml_comment_buffer.ToString (
				current_comment_start,
				length).Replace ("\r", "").Split ('\n');
			
			// The first line starts with /**, thus it is not target
			// for the format check.
			for (int i = 1; i < lines.Length; i++) {
				string s = lines [i];
				int idx = s.IndexOf ('*');
				string head = null;
				if (idx < 0) {
					if (i < lines.Length - 1)
						return;
					head = s;
				} else
					head = s.Substring (0, idx);
				foreach (char c in head)
					if (c != ' ')
						return;
				lines [i] = s.Substring (idx + 1);
			}
			xml_comment_buffer.Remove (current_comment_start, length);
			xml_comment_buffer.Insert (current_comment_start, String.Join (Environment.NewLine, lines));
		}

		//
		// Updates current comment location.
		//
		private void update_comment_location ()
		{
			if (current_comment_location.IsNull) {
				// "-2" is for heading "//" or "/*"
				current_comment_location =
					new Location (ref_line, hidden ? -1 : col - 2);
			}
		}

		//
		// Checks if there was incorrect doc comments and raise
		// warnings.
		//
		public void check_incorrect_doc_comment ()
		{
			if (xml_comment_buffer.Length > 0)
				warn_incorrect_doc_comment ();
		}

		//
		// Raises a warning when tokenizer found incorrect doccomment
		// markup.
		//
		private void warn_incorrect_doc_comment ()
		{
			if (doc_state != XmlCommentState.Error) {
				doc_state = XmlCommentState.Error;
				// in csc, it is 'XML comment is not placed on 
				// a valid language element'. But that does not
				// make sense.
				Report.Warning (1587, 2, Location, "XML comment is not placed on a valid language element");
			}
		}

		//
		// Consumes the saved xml comment lines (if any)
		// as for current target member or type.
		//
		public string consume_doc_comment ()
		{
			if (xml_comment_buffer.Length > 0) {
				string ret = xml_comment_buffer.ToString ();
				reset_doc_comment ();
				return ret;
			}
			return null;
		}

		Report Report {
			get { return context.Report; }
		}

		void reset_doc_comment ()
		{
			xml_comment_buffer.Length = 0;
			current_comment_location = Location.Null;
		}

		public void cleanup ()
		{
			if (ifstack != null && ifstack.Count >= 1) {
				int state = (int) ifstack.Pop ();
				if ((state & REGION) != 0)
					Report.Error (1038, Location, "#endregion directive expected");
				else 
					Report.Error (1027, Location, "Expected `#endif' directive");
			}
		}
	}

	//
	// Indicates whether it accepts XML documentation or not.
	//
	public enum XmlCommentState {
		// comment is allowed in this state.
		Allowed,
		// comment is not allowed in this state.
		NotAllowed,
		// once comments appeared when it is NotAllowed, then the
		// state is changed to it, until the state is changed to
		// .Allowed.
		Error
	}
}

