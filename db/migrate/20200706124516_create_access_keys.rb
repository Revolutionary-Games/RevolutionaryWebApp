class CreateAccessKeys < ActiveRecord::Migration[5.2]
  def change
    create_table :access_keys do |t|
      t.string :description
      t.timestamp :last_used
      t.string :key_code
      t.integer :key_type

      t.timestamps
    end
    add_index :access_keys, :key_code, unique: true
  end
end
