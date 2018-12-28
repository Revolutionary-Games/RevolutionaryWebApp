class AddNameForDebugSymbol < ActiveRecord::Migration[5.2]
  def change
    add_column :debug_symbols, :name, :string
    add_index :debug_symbols, [:name, :hash], unique: true
  end
end
