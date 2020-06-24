class AddSuspendedReasonToPatron < ActiveRecord::Migration[5.2]
  def change
    add_column :patrons, :suspended_reason, :string
  end
end
