# frozen_string_literal: true

class CreatePatrons < ActiveRecord::Migration[5.2]
  def change
    create_table :patrons do |t|
      t.boolean :suspended
      t.string :username
      t.string :email
      t.integer :pledge_amount_cents
      t.string :email_alias
      t.string :patreon_token
      t.string :patreon_refresh_token

      t.timestamps
    end
    add_index :patrons, :email, unique: true
    add_index :patrons, :email_alias, unique: true
  end
end
