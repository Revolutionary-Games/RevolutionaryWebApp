class CreateDehydratedObjects < ActiveRecord::Migration[5.2]
  def change
    create_table :dehydrated_objects do |t|
      t.string :sha3
      t.references :storage_item, foreign_key: true

      t.timestamps
    end
    add_index :dehydrated_objects, :sha3, unique: true
  end
end
