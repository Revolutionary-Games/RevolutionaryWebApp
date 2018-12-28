class CreateDebugSymbols < ActiveRecord::Migration[5.2]
  def change
    create_table :debug_symbols do |t|
      t.string :hash
      t.string :path

      t.timestamps
    end
  end
end
