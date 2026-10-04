Feature: libtab-compatible ndb tables
  libtab tables are Plan 9 ndb-shaped text: a schema tuple naming the columns,
  then one tuple entry per visible row. The managed implementation has to agree
  with the C library because typed cells are verified over exact bytes.

  Scenario: A table says what it holds
    Given the table
      """
      schema=weapons
      	col=name
      	col=cost

      name=autocannon
      	cost=1
      """
    Then the schema should be "weapons"
    And it should declare the columns "name, cost"

  Scenario: Rows preserve duplicate heads when the whole row differs
    Given the table
      """
      schema=weapons
      	col=name
      	col=cost

      name=autocannon
      	cost=1

      name=autocannon
      	cost=2
      """
    Then there should be 2 rows
    And the rows named "autocannon" should have costs "1, 2"

  Scenario: A column a row does not carry reads as absent
    Given the table
      """
      schema=weapons
      	col=name
      	col=note

      name=autocannon
      """
    Then the row named "autocannon" should have no "note"

  Scenario: The raw word nil is semantic nil
    Given the table
      """
      schema=weapons
      	col=name
      	col=note

      name=autocannon
      	note=nil
      """
    Then the row named "autocannon" should have no "note"

  Scenario: The literal word nil is encoded on write
    Given a value of "nil"
    Then encoding it should give "n&#105;l"
    And decoding that should give it back

  Scenario: Comments and blank lines are skipped
    Given the table
      """
      # weapons

      schema=weapons
      	col=name

      # cheap weapon
      name=autocannon
      """
    Then the schema should be "weapons"
    And there should be 1 rows

  Scenario: Rows cannot use undeclared columns
    When parsing the table
      """
      schema=weapons
      	col=name

      name=autocannon
      	cost=1
      """
    Then parsing should be refused

  Scenario: Typed cells must carry the declared tag
    When parsing the table
      """
      schema=accounts
      	col=name
      	col=password type=HASHED

      name=scott
      	password=plain
      """
    Then parsing should be refused

  Scenario: BLAKE2b hashed cells verify only their preimage
    Given a table with a HASHED column
    When I store the hash of "secret"
    Then the hash should verify "secret"
    And the hash should not verify "wrong"

  Scenario: Signed cells verify with the Monocypher public key
    Given the pinned Monocypher key
    And a table with a SIGNED column
    When I sign "vector"
    Then the public key should be the pinned public key
    And the signed body should verify as "vector"

  Scenario: Edited signed cells are refused
    Given the pinned Monocypher key
    And a table with a SIGNED column
    When I sign "vector"
    And I edit the signed cell body on disk
    Then signed verification should be refused
